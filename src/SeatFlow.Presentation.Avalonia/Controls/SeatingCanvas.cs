using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace SeatFlow.Presentation.Avalonia.Controls;

/// <summary>座位点击/激活事件参数。</summary>
public sealed class SeatEventArgs(string seatId, SeatVisual? seat) : EventArgs
{
    public string SeatId { get; } = seatId;
    public SeatVisual? Seat { get; } = seat;
}

/// <summary>座位拖拽放下事件参数（内部拖拽：座位→座位 / 座位→空白）。</summary>
public sealed class SeatDropEventArgs(string sourceSeatId, string? targetSeatId, string? studentId, Point pointerPosition) : EventArgs
{
    public string SourceSeatId { get; } = sourceSeatId;
    public string? TargetSeatId { get; } = targetSeatId;
    public string? StudentId { get; } = studentId;

    /// <summary>放下时指针相对画布的位置（用于垃圾桶等外围命中判定）。</summary>
    public Point PointerPosition { get; } = pointerPosition;

    /// <summary>由监听方判定：是否落在画布内的垃圾桶等删除区域。</summary>
    public bool IsOverTrash { get; set; }
}

/// <summary>
/// 自绘座位画布（06 计划 §5 方案 C）：
/// - 单控件绘制全部座位；按状态合批为少量 GeometryGroup（300–800 座不产生逐座位控件/绘制调用）；
/// - 文本 FormattedText 缓存 + 视口裁剪 + 小尺寸跳过标签（WASM 解释器/软件渲染下的关键优化）；
/// - 几何命中测试、内部拖拽、缩放/平移矩阵、键盘虚拟焦点；
/// - 渲染输入为不可变 <see cref="SeatLayoutSnapshot"/>，引用替换即重绘。
/// </summary>
public sealed class SeatingCanvas : Control
{
    /// <summary>板面四周留白（方向 B 方格纸画布）。</summary>
    public const double BoardMargin = 24;

    /// <summary>缩放范围（与设计一致 0.2–3.0）。</summary>
    public const double MinZoom = 0.2;
    public const double MaxZoom = 3.0;

    /// <summary>拖拽判定阈值（像素）。</summary>
    private const double DragThreshold = 4;

    /// <summary>座位标签最小可读屏幕宽度（低于则跳过绘制，保护大班级性能）。</summary>
    private const double LabelMinScreenWidth = 18;

    /// <summary>手势期 LOD 触发的座位数阈值：不超过该规模时拖拽/缩放仍完整绘制座位姓名。</summary>
    private const int GestureLodSeatThreshold = 150;

    public static readonly StyledProperty<SeatLayoutSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<SeatingCanvas, SeatLayoutSnapshot?>(nameof(Snapshot));

    public static readonly StyledProperty<string?> SelectedSeatIdProperty =
        AvaloniaProperty.Register<SeatingCanvas, string?>(nameof(SelectedSeatId));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<SeatingCanvas, double>(nameof(Zoom), 1.0, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<Vector> PanOffsetProperty =
        AvaloniaProperty.Register<SeatingCanvas, Vector>(nameof(PanOffset), default, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// 板面尺寸变化时是否自动适配视口（默认开启，供预览场景全览）。
    /// 工作台画布关闭该行为，由设置的「座位图默认缩放」驱动 <see cref="Zoom"/>。
    /// </summary>
    public static readonly StyledProperty<bool> AutoFitProperty =
        AvaloniaProperty.Register<SeatingCanvas, bool>(nameof(AutoFit), true);

    static SeatingCanvas()
    {
        AffectsRender<SeatingCanvas>(SnapshotProperty, SelectedSeatIdProperty, ZoomProperty, PanOffsetProperty);
        FocusableProperty.OverrideDefaultValue<SeatingCanvas>(true);
    }

    public SeatingCanvas()
    {
        ClipToBounds = true;
        DragDrop.SetAllowDrop(this, true);
        ActualThemeVariantChanged += (_, _) =>
        {
            _palette = null;
            _textCache.Clear();
            _gridGeometry = null;
            InvalidateVisual();
        };
    }

    // ─────────────── 公开属性 ───────────────

    /// <summary>座位布局快照（整体替换触发重绘）。</summary>
    public SeatLayoutSnapshot? Snapshot
    {
        get => GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    /// <summary>当前选中/焦点座位 ID（键盘导航与视觉选中；空表示无）。</summary>
    public string? SelectedSeatId
    {
        get => GetValue(SelectedSeatIdProperty);
        set => SetValue(SelectedSeatIdProperty, value);
    }

    /// <summary>缩放比例（0.2–3.0，双向绑定）。</summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, Math.Clamp(value, MinZoom, MaxZoom));
    }

    /// <summary>平移偏移（双向绑定；0 表示居中/贴边）。</summary>
    public Vector PanOffset
    {
        get => GetValue(PanOffsetProperty);
        set => SetValue(PanOffsetProperty, value);
    }

    /// <summary>板面尺寸变化时是否自动适配视口（预览场景开启；工作台关闭以使用默认缩放）。</summary>
    public bool AutoFit
    {
        get => GetValue(AutoFitProperty);
        set => SetValue(AutoFitProperty, value);
    }

    /// <summary>
    /// spike #2 诊断开关：开启后每次渲染输出 "[SF-CANVAS] render …ms seats=…" 到控制台
    /// （仅用于性能采样，默认关闭）。
    /// </summary>
    public bool DiagnosticsEnabled { get; set; }

    // ─────────────── 事件 ───────────────

    /// <summary>座位被点击（未发生拖拽时）。</summary>
    public event EventHandler<SeatEventArgs>? SeatClicked;

    /// <summary>座位被激活（键盘 Enter）。</summary>
    public event EventHandler<SeatEventArgs>? SeatActivated;

    /// <summary>内部拖拽放下（target 为 null 表示落在空白处，可由监听方判定垃圾桶等区域）。</summary>
    public event EventHandler<SeatDropEventArgs>? SeatDropped;

    // ─────────────── 公开方法 ───────────────

    /// <summary>以画布坐标命中座位（从上到下；无命中返回 null）。</summary>
    public SeatVisual? HitTestSeat(Point point)
    {
        var board = ToBoard(point);
        var seats = Snapshot?.Seats;
        if (seats is null) return null;

        for (var i = seats.Count - 1; i >= 0; i--)
        {
            var seat = seats[i];
            if (board.X >= seat.X && board.X <= seat.X + seat.Width &&
                board.Y >= seat.Y && board.Y <= seat.Y + seat.Height)
                return seat;
        }

        return null;
    }

    /// <summary>外部拖拽悬停目标高亮（null 清除）。</summary>
    public void SetDropTarget(string? seatId)
    {
        if (string.Equals(_externalDropTargetId, seatId, StringComparison.Ordinal))
            return;
        _externalDropTargetId = seatId;
        InvalidateVisual();
    }

    /// <summary>围绕指定点缩放（保持该点在光标下不漂移）。</summary>
    public void ZoomAt(double factor, Point center)
    {
        var oldZoom = Zoom;
        var newZoom = Math.Clamp(oldZoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001) return;

        var snapshot = Snapshot;
        var oldOffset = GetContentOffsetFor(Bounds.Size, oldZoom, snapshot) + PanOffset;
        var boardX = (center.X - oldOffset.X) / oldZoom;
        var boardY = (center.Y - oldOffset.Y) / oldZoom;
        var newContentOffset = GetContentOffsetFor(Bounds.Size, newZoom, snapshot);

        Zoom = newZoom;
        PanOffset = new Vector(
            center.X - (boardX * newZoom) - newContentOffset.X,
            center.Y - (boardY * newZoom) - newContentOffset.Y);
        InvalidateVisual();
    }

    /// <summary>缩放到适应视口（上限 1.0，不放大）。</summary>
    public void FitToView()
    {
        var snapshot = Snapshot;
        if (snapshot is null || snapshot.BoardWidth <= 0 || snapshot.BoardHeight <= 0)
        {
            Zoom = 1.0;
            PanOffset = default;
            return;
        }

        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;

        var scale = Math.Min(
            (size.Width - (BoardMargin * 2)) / snapshot.BoardWidth,
            (size.Height - (BoardMargin * 2)) / snapshot.BoardHeight);
        Zoom = Math.Clamp(Math.Min(scale, 1.0), MinZoom, 1.0);
        PanOffset = default;
        InvalidateVisual();
    }

    /// <summary>按屏幕像素平移。</summary>
    public void PanBy(Vector delta)
    {
        PanOffset += delta;
        InvalidateVisual();
    }

    // ─────────────── 渲染 ───────────────

    protected override Size MeasureOverride(Size availableSize) => default;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AutoFitProperty && !AutoFit)
        {
            // 关闭自动适配时清除待执行标记，避免后续渲染帧仍执行 fit
            _autoFitPending = false;
        }

        if (change.Property == SnapshotProperty)
        {
            var oldSnapshot = change.GetOldValue<SeatLayoutSnapshot?>();
            var newSnapshot = change.GetNewValue<SeatLayoutSnapshot?>();

            // 视图缓存（M3）下控件可能已处于可见状态：快照替换必须显式重绘，
            // 否则回滚/再次生成后的画布不会更新（M4 实机定位）。
            InvalidateVisual();

            if (newSnapshot is null)
            {
                ResetLabelGeometry();
                return;
            }

            if (oldSnapshot is null
                || !ReferenceEquals(oldSnapshot, newSnapshot))
            {
                // 标签矢量化几何需要重建（渐进式，见 EnsureLabelGeometry）
                ResetLabelGeometry();
            }

            if (oldSnapshot is null
                || Math.Abs(oldSnapshot.BoardWidth - newSnapshot.BoardWidth) > 0.5
                || Math.Abs(oldSnapshot.BoardHeight - newSnapshot.BoardHeight) > 0.5)
            {
                _autoFitPending = true;
            }        }
    }

    public override void Render(DrawingContext context)
    {
        var palette = EnsurePalette();
        var bounds = new Rect(Bounds.Size);

        // 方格纸底 + 发丝格线（固定于视口，与 html 样机一致）
        context.FillRectangle(palette.CanvasBg, bounds);
        context.DrawGeometry(null, palette.GridPen, GetGridGeometry(bounds));

        var snapshot = Snapshot;
        if (snapshot is null || snapshot.Seats.Count == 0)
            return;

        // 首次或板面尺寸变化：适应视口（不能在渲染过程中改属性 → 调度到渲染后执行）；
        // AutoFit=false（工作台）时由外部默认缩放驱动，不做适配。
        if (AutoFit && _autoFitPending && bounds.Width > 1 && bounds.Height > 1)
        {
            if (!_autoFitScheduled)
            {
                _autoFitScheduled = true;
                Dispatcher.UIThread.Post(() =>
                {
                    _autoFitScheduled = false;
                    _autoFitPending = false;
                    FitToView();
                }, DispatcherPriority.Background);
            }
            return;
        }

        var zoom = Zoom;
        var contentOffset = GetContentOffsetFor(Bounds.Size, zoom, snapshot) + PanOffset;

        var sw = DiagnosticsEnabled ? System.Diagnostics.Stopwatch.StartNew() : null;
        using (context.PushTransform(Matrix.CreateScale(zoom, zoom) * Matrix.CreateTranslation(contentOffset.X, contentOffset.Y)))
        {
            var boardRect = new Rect(0, 0, snapshot.BoardWidth, snapshot.BoardHeight);
            context.DrawRectangle(palette.BoardBg, palette.BoardPen, boardRect, 8, 8);

            if (snapshot.Overlays is { Count: > 0 })
            {
                foreach (var overlay in snapshot.Overlays)
                    DrawOverlay(context, overlay, palette);
            }

            DrawSeats(context, snapshot, palette, zoom, contentOffset);
            DrawDragGhost(context, palette);
        }

        if (sw is not null)
        {
            Console.WriteLine(
                $"[SF-CANVAS] render {sw.Elapsed.TotalMilliseconds:F1}ms seats={snapshot.Seats.Count} zoom={zoom:F2} viewport={Bounds.Width:F0}x{Bounds.Height:F0}");
        }
    }

    /// <summary>方格纸网格：按视口尺寸缓存为单条几何，避免逐帧数十次 DrawLine 调用。</summary>
    private GeometryGroup GetGridGeometry(Rect bounds)
    {
        if (_gridGeometry is not null && _gridGeometryFor == bounds.Size)
            return _gridGeometry;

        const double step = 24;
        var group = new GeometryGroup();
        for (double x = step; x < bounds.Width; x += step)
            group.Children.Add(new LineGeometry(new Point(x, 0), new Point(x, bounds.Height)));
        for (double y = step; y < bounds.Height; y += step)
            group.Children.Add(new LineGeometry(new Point(0, y), new Point(bounds.Width, y)));

        _gridGeometry = group;
        _gridGeometryFor = bounds.Size;
        return group;
    }

    private void DrawOverlay(DrawingContext context, BoardOverlay overlay, Palette palette)
    {
        var rect = new Rect(overlay.X, overlay.Y, overlay.Width, overlay.Height);
        var corner = overlay.IsRound ? overlay.Height / 2 : 4;
        context.DrawRectangle(palette.PodiumBg, palette.OverlayPen, rect, corner, corner);

        if (!string.IsNullOrEmpty(overlay.Label))
        {
            var text = ResolveText(overlay.Label, 11, palette.TextFaint, TextSlot.Faint);
            context.DrawText(text, new Point(
                rect.X + ((rect.Width - text.Width) / 2),
                rect.Y + ((rect.Height - text.Height) / 2)));
        }
    }

    /// <summary>
    /// 座位绘制：按状态合批（普通/占用/选中/禁用 各一次 DrawGeometry），
    /// 文本使用缓存，视口外裁剪，过小尺寸跳过标签。
    /// </summary>
    private void DrawSeats(DrawingContext context, SeatLayoutSnapshot snapshot, Palette palette, double zoom, Point contentOffset)
    {
        // 视口对应的板面区域（含少量余量）
        var viewport = new Rect(
            -contentOffset.X / zoom,
            -contentOffset.Y / zoom,
            Bounds.Width / zoom,
            Bounds.Height / zoom).Inflate(8);

        GeometryGroup? normal = null;
        GeometryGroup? occupied = null;
        GeometryGroup? selected = null;
        GeometryGroup? disabled = null;
        GeometryGroup? fixedDots = null;

        foreach (var seat in snapshot.Seats)
        {
            var rect = new Rect(seat.X, seat.Y, seat.Width, seat.Height);
            if (!viewport.Intersects(rect))
                continue;

            var isSelected = seat.IsSwapSource || string.Equals(seat.Id, SelectedSeatId, StringComparison.Ordinal);
            var isDragSource = string.Equals(seat.Id, _dragSeatId, StringComparison.Ordinal);
            var isDropTarget = seat.IsDropTarget
                || string.Equals(seat.Id, _externalDropTargetId, StringComparison.Ordinal)
                || string.Equals(seat.Id, _dragTargetId, StringComparison.Ordinal);

            GeometryGroup target;
            if (seat.IsDisabled) target = disabled ??= new GeometryGroup();
            else if (isSelected) target = selected ??= new GeometryGroup();
            else if (seat.IsOccupied) target = occupied ??= new GeometryGroup();
            else target = normal ??= new GeometryGroup();
            target.Children.Add(new RectangleGeometry(rect, 5, 5));

            if (seat.IsFixed)
            {
                fixedDots ??= new GeometryGroup();
                fixedDots.Children.Add(new EllipseGeometry(new Rect(rect.Right - 8.5, rect.Top + 3.5, 5, 5)));
            }

            if (seat.IsDataStale)
                context.DrawRectangle(null, palette.StalePen, rect.Inflate(0.5), 6, 6);

            if (isDragSource && _isDragging)
            {
                using (context.PushOpacity(0.55))
                    context.DrawRectangle(palette.BoardBg, null, rect, 5, 5);
            }

            if (isDropTarget && !isDragSource)
            {
                using (context.PushOpacity(0.45))
                    context.DrawRectangle(palette.AccentSoft, palette.DropTargetPen, rect.Inflate(1), 6, 6);
            }

        }

        // 手势期间降级：仅填充（不描边）；大布局文本延后到手势结束。
        // 常规教室规模（≤ GestureLodSeatThreshold 座）保持完整文本渲染——用户实测反馈：
        // 拖拽时其余座位姓名消失影响定位（WASM 软件渲染下文本最贵，仅大布局承担 LOD）。
        var lod = _gestureActive && snapshot.Seats.Count > GestureLodSeatThreshold;
        if (normal is not null) context.DrawGeometry(palette.SeatBg, lod ? null : palette.SeatPen, normal);
        if (occupied is not null) context.DrawGeometry(palette.SeatOccupiedBg, lod ? null : palette.SeatOccupiedPen, occupied);
        if (disabled is not null) context.DrawGeometry(palette.SeatDisabledBg, lod ? null : palette.SeatDisabledPen, disabled);
        if (selected is not null) context.DrawGeometry(palette.AccentSoft, palette.AccentPen, selected);
        if (fixedDots is not null) context.DrawGeometry(palette.FixedDot, null, fixedDots);

        if (lod) return;

        // 标签矢量化：渐进构建一次，之后每帧仅一次 DrawGeometry（缩放/平移零成本）
        EnsureLabelGeometry(snapshot, zoom, palette);
        if (_labelGeometry is not null)
            context.DrawGeometry(palette.SeatLabelFg, null, _labelGeometry);
    }

    private void ResetLabelGeometry()
    {
        _labelGeometry = null;
        _labelBuildIndex = 0;
        _labelBuildScheduled = false;
        _labelGeometryZoom = -1;
    }

    /// <summary>
    /// 渐进构建座位标签的矢量几何（每帧最多约 24 条 / 扫描 240 个座位），
    /// 构建完成后为单次 DrawGeometry；文本几何随画布矩阵缩放，无需按缩放重建。
    /// </summary>
    private void EnsureLabelGeometry(SeatLayoutSnapshot snapshot, double zoom, Palette palette)
    {
        if (Math.Abs(zoom - _labelGeometryZoom) > 0.05)
        {
            _labelGeometry = null;
            _labelBuildIndex = 0;
            _labelGeometryZoom = zoom;
        }

        if (_labelBuildScheduled)
            return;

        if (_labelBuildIndex >= snapshot.Seats.Count && _labelGeometry is not null)
            return;

        _labelGeometry ??= new GeometryGroup();

        const int labelBatch = 12;
        var built = 0;

        while (_labelBuildIndex < snapshot.Seats.Count && built < labelBatch)
        {
            var seat = snapshot.Seats[_labelBuildIndex++];

            if (!seat.IsOccupied || string.IsNullOrEmpty(seat.Label) || seat.Width * zoom < LabelMinScreenWidth)
                continue;

            // 座位姓名基础自动换行（最多两行，超出省略）：宽度约束到座位内，高度约束到约两行
            var maxWidth = Math.Max(8, seat.Width - 6);
            var maxHeight = Math.Max(8, Math.Min(seat.Height - 4, 26));
            var text = ResolveText(seat.Label, 10, palette.SeatLabelFg, TextSlot.Seat, maxWidth, maxHeight);
            var origin = new Point(
                seat.X + ((seat.Width - text.Width) / 2),
                seat.Y + ((seat.Height - text.Height) / 2));
            var geometry = text.BuildGeometry(origin);
            if (geometry is not null)
                _labelGeometry.Children.Add(geometry);
            built++;
        }

        if (_labelBuildIndex < snapshot.Seats.Count)
        {
            _labelBuildScheduled = true;
            Dispatcher.UIThread.Post(() =>
            {
                _labelBuildScheduled = false;
                InvalidateVisual();
            }, DispatcherPriority.Background);
        }
    }

    private void DrawDragGhost(DrawingContext context, Palette palette)
    {
        if (!_isDragging || _dragSeatId is null) return;

        var seat = FindSeat(_dragSeatId);
        if (seat is null) return;

        var x = _dragBoardPosition.X - _dragGrabOffset.X;
        var y = _dragBoardPosition.Y - _dragGrabOffset.Y;
        var rect = new Rect(x, y, seat.Width, seat.Height);
        context.DrawRectangle(palette.GhostBg, palette.DropTargetPen, rect, 5, 5);

        if (!string.IsNullOrEmpty(seat.Label))
        {
            var text = ResolveText(seat.Label, 10, palette.SeatLabelFg, TextSlot.Seat,
                Math.Max(8, seat.Width - 6), Math.Max(8, Math.Min(seat.Height - 4, 26)));
            context.DrawText(text, new Point(
                rect.X + ((rect.Width - text.Width) / 2),
                rect.Y + ((rect.Height - text.Height) / 2)));
        }
    }

    // ─────────────── 变换与命中 ───────────────

    private Point ToBoard(Point screen)
    {
        var zoom = Zoom;
        var offset = GetContentOffsetFor(Bounds.Size, zoom, Snapshot) + PanOffset;
        return new Point((screen.X - offset.X) / zoom, (screen.Y - offset.Y) / zoom);
    }

    private static Point GetContentOffsetFor(Size viewport, double zoom, SeatLayoutSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.BoardWidth <= 0 || snapshot.BoardHeight <= 0)
            return default;

        var contentWidth = snapshot.BoardWidth * zoom;
        var contentHeight = snapshot.BoardHeight * zoom;
        var x = contentWidth + (BoardMargin * 2) < viewport.Width
            ? (viewport.Width - contentWidth) / 2
            : BoardMargin;
        var y = contentHeight + (BoardMargin * 2) < viewport.Height
            ? (viewport.Height - contentHeight) / 2
            : BoardMargin;
        return new Point(x, y);
    }

    private SeatVisual? FindSeat(string id)
    {
        var seats = Snapshot?.Seats;
        if (seats is null) return null;
        foreach (var seat in seats)
        {
            if (string.Equals(seat.Id, id, StringComparison.Ordinal))
                return seat;
        }
        return null;
    }

    // ─────────────── 指针交互 ───────────────

    private string? _dragSeatId;
    private string? _dragTargetId;
    private bool _isDragging;
    private bool _isPanning;
    private Point _pointerPosition;
    private Point _pressPosition;
    private Point _dragBoardPosition;
    private Vector _dragGrabOffset;
    private Vector _panLastPosition;
    private string? _externalDropTargetId;
    private bool _isHoveringSeat;
    private bool _autoFitPending = true;
    private bool _autoFitScheduled;
    private bool _gestureActive;
    private DispatcherTimer? _gestureSettleTimer;
    private GeometryGroup? _gridGeometry;
    private Size _gridGeometryFor;
    private GeometryGroup? _labelGeometry;
    private int _labelBuildIndex;
    private bool _labelBuildScheduled;
    private double _labelGeometryZoom = -1;

    /// <summary>
    /// 手势期间降级渲染（LOD）：跳过文本与描边，仅绘制填充；
    /// 手势结束后 200ms 恢复完整渲染（WASM 软件渲染下文本是最贵的一环）。
    /// </summary>
    private void BeginGesture()
    {
        if (_gestureActive) return;
        _gestureActive = true;
        InvalidateVisual();
    }

    private void EndGestureSoon()
    {
        _gestureSettleTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _gestureSettleTimer.Stop();
        _gestureSettleTimer.Tick -= OnGestureSettle;
        _gestureSettleTimer.Tick += OnGestureSettle;
        _gestureSettleTimer.Start();
    }

    private void OnGestureSettle(object? sender, EventArgs e)
    {
        _gestureSettleTimer?.Stop();
        _gestureActive = false;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        BeginGesture();

        Focus();
        _pointerPosition = e.GetPosition(this);
        _pressPosition = _pointerPosition;

        var seat = HitTestSeat(_pointerPosition);
        if (seat is not null && !seat.IsDisabled)
        {
            _dragSeatId = seat.Id;
            _dragTargetId = null;
            _isDragging = false;
            _dragBoardPosition = ToBoard(_pointerPosition);
            _dragGrabOffset = new Vector(_dragBoardPosition.X - seat.X, _dragBoardPosition.Y - seat.Y);
            SelectedSeatId = seat.Id;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // 空白处：平移
        _isPanning = true;
        _panLastPosition = new Vector(_pointerPosition.X, _pointerPosition.Y);
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _pointerPosition = e.GetPosition(this);

        if (_isPanning)
        {
            var current = new Vector(_pointerPosition.X, _pointerPosition.Y);
            PanBy(current - _panLastPosition);
            _panLastPosition = current;
            return;
        }

        if (_dragSeatId is null)
        {
            var hover = HitTestSeat(_pointerPosition) is not null;
            if (hover != _isHoveringSeat)
            {
                _isHoveringSeat = hover;
                Cursor = hover ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow);
            }
            return;
        }

        if (!_isDragging &&
            (Math.Abs(_pointerPosition.X - _pressPosition.X) > DragThreshold ||
             Math.Abs(_pointerPosition.Y - _pressPosition.Y) > DragThreshold))
        {
            var seat = FindSeat(_dragSeatId);
            if (seat is null || seat.IsFixed)
            {
                return;
            }
            _isDragging = true;
            Cursor = new Cursor(StandardCursorType.DragMove);
        }

        if (_isDragging)
        {
            _dragBoardPosition = ToBoard(_pointerPosition);
            var target = HitTestSeat(_pointerPosition);
            _dragTargetId = target is not null && !string.Equals(target.Id, _dragSeatId, StringComparison.Ordinal) && !target.IsDisabled
                ? target.Id
                : null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isPanning)
        {
            _isPanning = false;
            Cursor = new Cursor(StandardCursorType.Arrow);
            e.Pointer.Capture(null);
            EndGestureSoon();
            return;
        }

        if (_dragSeatId is { } seatId)
        {
            var seat = FindSeat(seatId);
            var pointer = e.GetPosition(this);

            if (_isDragging && seat is not null)
            {
                var target = HitTestSeat(pointer);
                var targetId = target is not null
                    && !string.Equals(target.Id, seatId, StringComparison.Ordinal)
                    && !target.IsDisabled
                        ? target.Id
                        : null;

                SeatDropped?.Invoke(this, new SeatDropEventArgs(seatId, targetId, seat.StudentId, pointer));
            }
            else
            {
                SeatClicked?.Invoke(this, new SeatEventArgs(seatId, seat));
            }

            _dragSeatId = null;
            _dragTargetId = null;
            _isDragging = false;
            _dragBoardPosition = default;
            _dragGrabOffset = default;
            Cursor = new Cursor(StandardCursorType.Arrow);
            InvalidateVisual();
        }

        e.Pointer.Capture(null);
        EndGestureSoon();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        BeginGesture();
        var position = e.GetPosition(this);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ZoomAt(e.Delta.Y > 0 ? 1.1 : 1 / 1.1, position);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            PanBy(new Vector(e.Delta.Y * 48, 0));
        }
        else
        {
            PanBy(new Vector(e.Delta.X * 48, e.Delta.Y * 48));
        }

        e.Handled = true;
        EndGestureSoon();
    }

    // ─────────────── 键盘虚拟焦点 ───────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var seats = Snapshot?.Seats;
        if (seats is null || seats.Count == 0) return;

        var current = SelectedSeatId is { } id ? FindSeat(id) : null;

        switch (e.Key)
        {
            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                {
                    var direction = e.Key switch
                    {
                        Key.Left => new Vector(-1, 0),
                        Key.Right => new Vector(1, 0),
                        Key.Up => new Vector(0, -1),
                        _ => new Vector(0, 1),
                    };

                    var next = current is null
                        ? seats.FirstOrDefault(s => !s.IsDisabled)
                        : FindDirectionalNeighbour(seats, current, direction);
                    if (next is not null)
                    {
                        SelectedSeatId = next.Id;
                        EnsureSeatVisible(next);
                        InvalidateVisual();
                        e.Handled = true;
                    }
                    break;
                }

            case Key.Enter:
            case Key.Space:
                if (_dragSeatId is null && SelectedSeatId is { } selected)
                {
                    var seat = FindSeat(selected);
                    if (seat is not null)
                    {
                        SeatActivated?.Invoke(this, new SeatEventArgs(selected, seat));
                        e.Handled = true;
                    }
                }
                break;
        }
    }

    private static SeatVisual? FindDirectionalNeighbour(IReadOnlyList<SeatVisual> seats, SeatVisual current, Vector direction)
    {
        var cx = current.X + (current.Width / 2);
        var cy = current.Y + (current.Height / 2);

        SeatVisual? best = null;
        var bestScore = double.MaxValue;

        foreach (var seat in seats)
        {
            if (seat.IsDisabled || ReferenceEquals(seat, current) || seat.Id == current.Id) continue;

            var dx = seat.X + (seat.Width / 2) - cx;
            var dy = seat.Y + (seat.Height / 2) - cy;
            var forward = (dx * direction.X) + (dy * direction.Y);
            if (forward <= 1) continue;

            var sideways = Math.Abs((dx * direction.Y) - (dy * direction.X));
            if (sideways > forward * 1.6) continue;

            var score = forward + (sideways * 2.2);
            if (score < bestScore)
            {
                bestScore = score;
                best = seat;
            }
        }

        return best;
    }

    private void EnsureSeatVisible(SeatVisual seat)
    {
        var zoom = Zoom;
        var offset = GetContentOffsetFor(Bounds.Size, zoom, Snapshot) + PanOffset;
        var centerX = (seat.X + (seat.Width / 2)) * zoom + offset.X;
        var centerY = (seat.Y + (seat.Height / 2)) * zoom + offset.Y;

        var dx = 0.0;
        var dy = 0.0;

        if (centerX < 40) dx = 40 - centerX;
        else if (centerX > Bounds.Width - 40) dx = Bounds.Width - 40 - centerX;

        if (centerY < 40) dy = 40 - centerY;
        else if (centerY > Bounds.Height - 40) dy = Bounds.Height - 40 - centerY;

        if (dx != 0 || dy != 0)
            PanBy(new Vector(dx, dy));
    }

    // ─────────────── 文本缓存 ───────────────

    private enum TextSlot
    {
        Seat = 0,
        Faint = 1,
    }

    private static readonly Typeface SeatTypeface = new(FontFamily.Default);
    private readonly Dictionary<(string Text, double Size, int Slot, int MaxWidth, int MaxHeight), FormattedText> _textCache = new();

    private static FormattedText CreateText(string text, double size, IBrush brush)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, SeatTypeface, size, brush);

    private FormattedText ResolveText(string text, double size, IBrush brush, TextSlot slot,
        double? maxTextWidth = null, double? maxTextHeight = null)
    {
        if (_textCache.Count > 3000) _textCache.Clear();
        var key = (text, size, (int)slot, (int)Math.Round(maxTextWidth ?? 0), (int)Math.Round(maxTextHeight ?? 0));
        if (!_textCache.TryGetValue(key, out var formatted))
        {
            formatted = CreateText(text, size, brush);
            if (maxTextWidth is { } width)
            {
                // 基础自动换行（最多两行）：宽度约束到容器，超出省略；高度上限由调用方给出
                formatted.Trimming = TextTrimming.CharacterEllipsis;
                formatted.MaxTextWidth = width;
                formatted.MaxLineCount = 2;
            }
            else
            {
                formatted.MaxTextWidth = 1000;
            }

            formatted.MaxTextHeight = maxTextHeight ?? 1000;
            _textCache[key] = formatted;
        }
        return formatted;
    }

    // ─────────────── 主题画刷 ───────────────

    private Palette? _palette;

    private Palette EnsurePalette()
        => _palette ??= new Palette(
            Brush("SfCanvasBgBrush", Brushes.Transparent),
            Brush("SfCanvasGridBrush", Brushes.Transparent),
            Brush("SfBoardBgBrush", Brushes.White),
            Brush("SfBoardBorderBrush", Brushes.Gray),
            Brush("SfPodiumBgBrush", Brushes.Gainsboro),
            Brush("SfSeatBgBrush", Brushes.White),
            Brush("SfSeatBorderBrush", Brushes.Gray),
            Brush("SfSeatOccupiedBrush", Brushes.LightBlue),
            Brush("SfSeatOccupiedBorderBrush", Brushes.SteelBlue),
            Brush("SfSeatOccupiedFgBrush", Brushes.DarkBlue),
            Brush("SfAccentBrush", Brushes.DodgerBlue),
            Brush("SfAccentSoftBrush", Brushes.LightBlue),
            Brush("SfFixedBrush", Brushes.DodgerBlue),
            Brush("SfSurface2Brush", Brushes.Gainsboro),
            Brush("SfWarnBrush", Brushes.Orange),
            Brush("SfTextFaintBrush", Brushes.Gray));

    private IBrush Brush(string key, IBrush fallback)
        => this.TryFindResource(key, out var value) && value is IBrush brush ? brush : fallback;

    private sealed record Palette(
        IBrush CanvasBg,
        IBrush Grid,
        IBrush BoardBg,
        IBrush BoardBorder,
        IBrush PodiumBg,
        IBrush SeatBg,
        IBrush SeatBorder,
        IBrush SeatOccupiedBg,
        IBrush SeatOccupiedBorder,
        IBrush SeatLabelFg,
        IBrush Accent,
        IBrush AccentSoft,
        IBrush Fixed,
        IBrush Surface2,
        IBrush Warn,
        IBrush TextFaint)
    {
        public IPen GridPen { get; } = new Pen(Grid, 1);
        public IPen BoardPen { get; } = new Pen(BoardBorder, 1);
        public IPen SeatPen { get; } = new Pen(SeatBorder, 1);
        public IPen SeatOccupiedPen { get; } = new Pen(SeatOccupiedBorder, 1);
        public IPen SeatDisabledPen { get; } = new Pen(BoardBorder, 1, new DashStyle([3, 3], 0));
        public IPen AccentPen { get; } = new Pen(Accent, 1.5);
        public IPen DropTargetPen { get; } = new Pen(Accent, 2, new DashStyle([4, 3], 0));
        public IPen StalePen { get; } = new Pen(Warn, 2);
        public IPen OverlayPen { get; } = new Pen(BoardBorder, 1, new DashStyle([4, 4], 0));
        public IBrush SeatDisabledBg { get; } = Surface2;
        public IBrush FixedDot { get; } = Fixed;
        public IBrush GhostBg { get; } = AccentSoft;
    }
}
