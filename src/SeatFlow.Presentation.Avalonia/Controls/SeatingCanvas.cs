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

    /// <summary>平移收敛时每个方向至少保留的可见视口比例（板面较小时整块保留）。</summary>
    private const double MinVisibleViewportFraction = 0.75;

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

    /// <summary>禁用座位拾取模式：可点击禁用位且不触发拖拽（会场配置预览用）。</summary>
    public static readonly StyledProperty<bool> IsSeatPickingProperty =
        AvaloniaProperty.Register<SeatingCanvas, bool>(nameof(IsSeatPicking));

    static SeatingCanvas()
    {
        AffectsRender<SeatingCanvas>(
            SnapshotProperty, SelectedSeatIdProperty, IsSeatPickingProperty, ZoomProperty, PanOffsetProperty);
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
        set
        {
            var zoom = Math.Clamp(value, MinZoom, MaxZoom);
            SetValue(ZoomProperty, zoom);
            // 缩放变化会改变板面在视口中的基准位置：同步收敛平移量，
            // 否则缩小后旧的平移量会把会场整体推到视口外（看起来「消失」）。
            SetValue(PanOffsetProperty, ClampPan(PanOffset, zoom, Bounds.Size));
        }
    }

    /// <summary>平移偏移（双向绑定；0 表示居中/贴边）。</summary>
    public Vector PanOffset
    {
        get => GetValue(PanOffsetProperty);
        set => SetValue(PanOffsetProperty, ClampPan(value, Zoom, Bounds.Size));
    }

    /// <summary>板面尺寸变化时是否自动适配视口（预览场景开启；工作台关闭以使用默认缩放）。</summary>
    public bool AutoFit
    {
        get => GetValue(AutoFitProperty);
        set => SetValue(AutoFitProperty, value);
    }

    /// <summary>禁用座位拾取模式：可点击禁用位且不触发拖拽（会场配置预览用）。</summary>
    public bool IsSeatPicking
    {
        get => GetValue(IsSeatPickingProperty);
        set => SetValue(IsSeatPickingProperty, value);
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

    /// <summary>
    /// 触摸拖动中的学生姓名（null 表示没有拖拽）：在指针位置绘制姓名幽灵，
    /// 并高亮命中的座位。触摸设备不支持系统 DnD，拖名字必须由画布自己画。
    /// </summary>
    public void SetDragGhost(string? label, Point? pointerInCanvas)
    {
        _touchGhostLabel = label;
        _touchGhostPosition = pointerInCanvas;
        InvalidateVisual();
    }

    /// <summary>按画布坐标命中座位并返回（拖名字时外部按指针位置查询目标）。</summary>
    public SeatVisual? HitTestSeatAt(Point pointInCanvas) => HitTestSeat(pointInCanvas);

    /// <summary>手写笔悬停是否命中座位（测试/诊断用）。</summary>

    /// <summary>是否正在显示触摸拖名字的幽灵标签（测试用）。</summary>
    internal bool DragGhostVisibleForTest => !string.IsNullOrEmpty(_touchGhostLabel);

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

    /// <summary>
    /// 重置视图到指定缩放，并把平移量归零（点击缩放百分比时的行为）。
    /// 只改缩放不改平移会把之前的平移量保留下来，缩小后会场直接跑出视口。
    /// </summary>
    public void ResetView(double zoom)
    {
        var clamped = Math.Clamp(zoom, MinZoom, MaxZoom);
        SetValue(ZoomProperty, clamped);
        SetValue(PanOffsetProperty, default);
        InvalidateVisual();
    }

    /// <summary>按屏幕像素平移（自动限制在可见范围内，板面不会被拖丢）。</summary>
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
                // 换了会场/板面大小：旧平移量没有意义，直接回到默认位置，
                // 否则切换会场后可能看不到新会场（旧的平移把板面推到了视口外）。
                _autoFitPending = true;
                SetValue(PanOffsetProperty, default);
            }
            else
            {
                // 同一板面的快照替换（每次落座都会重建）：只收敛越界的平移量
                SetValue(PanOffsetProperty, ClampPan(PanOffset, Zoom, Bounds.Size));
            }
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);

        // 窗口尺寸变化后，旧的平移量可能超出新视口能接受的范围
        var clamped = ClampPan(PanOffset, Zoom, finalSize);
        if (clamped != PanOffset)
            SetCurrentValue(PanOffsetProperty, clamped);

        return arranged;
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

        DrawTouchGhost(context, palette);

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
        GeometryGroup? disabledSelected = null;
        GeometryGroup? fixedDots = null;

        foreach (var seat in snapshot.Seats)
        {
            var rect = new Rect(seat.X, seat.Y, seat.Width, seat.Height);
            if (!viewport.Intersects(rect))
                continue;

            var isSelected = seat.IsSwapSource || seat.IsSelected
                || (!IsSeatPicking && string.Equals(seat.Id, SelectedSeatId, StringComparison.Ordinal));
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

            if (isSelected && seat.IsDisabled)
            {
                disabledSelected ??= new GeometryGroup();
                disabledSelected.Children.Add(new RectangleGeometry(rect, 5, 5));
            }

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
        if (disabledSelected is not null) context.DrawGeometry(palette.AccentSoft, palette.AccentDashedPen, disabledSelected);
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

    /// <summary>
    /// 触摸拖名字的幽灵标签（屏幕坐标，不随板面缩放）：手指下方显示被拖动的姓名，
    /// 让「正拖着一个学生」这件事在触屏上可见。
    /// </summary>
    private void DrawTouchGhost(DrawingContext context, Palette palette)
    {
        if (string.IsNullOrEmpty(_touchGhostLabel) || _touchGhostPosition is not { } position)
            return;

        var text = ResolveText(_touchGhostLabel, 13, palette.SeatLabelFg, TextSlot.Faint, 220, 40);
        const double paddingX = 8;
        const double paddingY = 4;
        var rect = new Rect(
            position.X - (text.Width / 2) - paddingX,
            position.Y - (text.Height) - 14 - paddingY,
            text.Width + (paddingX * 2),
            text.Height + (paddingY * 2));

        context.DrawRectangle(palette.GhostBg, palette.DropTargetPen, rect, 6, 6);
        context.DrawText(text, new Point(rect.X + paddingX, rect.Y + paddingY));
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

    /// <summary>
    /// 收敛平移量，保证板面不会整块被拖出视口：
    /// 每个方向至少保留 <see cref="MinVisibleViewportFraction"/> 的视口（板面较小时则整块保留）可见。
    /// 边界是板面尺寸的连续函数，缩放过程中不会因跨过某个阈值而突然跳动；
    /// 同时不会把捏合锚点强行拉回中心，手指下方的座位仍能保持在原位。
    /// 没有这一步，平移量可以无限增大，会场会被拖到视口外且难以找回。
    /// </summary>
    private Vector ClampPan(Vector pan, double zoom, Size viewport)
    {
        var snapshot = Snapshot;
        if (snapshot is null || snapshot.BoardWidth <= 0 || snapshot.BoardHeight <= 0)
            return pan;
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return pan;

        var baseOffset = GetContentOffsetFor(viewport, zoom, snapshot);
        var contentWidth = snapshot.BoardWidth * zoom;
        var contentHeight = snapshot.BoardHeight * zoom;

        var left = baseOffset.X + pan.X;
        var top = baseOffset.Y + pan.Y;

        var minVisibleX = Math.Min(contentWidth, viewport.Width * MinVisibleViewportFraction);
        var minVisibleY = Math.Min(contentHeight, viewport.Height * MinVisibleViewportFraction);
        var minX = minVisibleX - contentWidth;
        var maxX = viewport.Width - minVisibleX;
        var minY = minVisibleY - contentHeight;
        var maxY = viewport.Height - minVisibleY;

        left = Math.Clamp(left, minX, maxX);
        top = Math.Clamp(top, minY, maxY);

        return new Vector(left - baseOffset.X, top - baseOffset.Y);
    }

    /// <summary>当前板面左上角在控件内的位置（测试/诊断用）。</summary>
    internal Point ContentOffsetForTest
        => GetContentOffsetFor(Bounds.Size, Zoom, Snapshot) + PanOffset;

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

    /// <summary>最近一次指针在画布内移动的位置（测试/诊断用）。</summary>
    internal Point LastPointerPositionForTest => _pointerPosition;

    /// <summary>最近一次指针事件收到的原始位置（窗口坐标，测试/诊断用）。</summary>
    internal Point RawPointerPositionForTest { get; private set; }
    private Point _pressPosition;
    private Point _dragBoardPosition;
    private Vector _dragGrabOffset;
    private Vector _panLastPosition;
    /// <summary>当前手势的归属指针（触摸/鼠标/手写笔）；非 null 时忽略其它指针的按下。</summary>
    private IPointer? _activePointer;
    private bool _isPointerPressed;

    // ── 双指缩放（触摸设备没有滚轮，捏合是唯一顺手的缩放手势） ──
    /// <summary>第一根手指：双指缩放的锚点参照之一。</summary>
    private IPointer? _pinchFirstPointer;
    private Point _pinchFirstPosition;
    /// <summary>第二根手指：必须与 <see cref="SeatingCanvas._pinchFirstPointer"/> 分开记位置，
    /// 否则任一根手指移动都会互相覆盖，两指间距瞬间归零 → 画布缩放跳变（抽搐/消失的根因）。</summary>
    private IPointer? _pinchSecondPointer;
    private Point _pinchSecondPosition;
    /// <summary>双指缩放中：真 = 正在按两根手指的间距缩放。</summary>
    private bool _pinchActive;
    /// <summary>双指手势已识别（期间忽略第三根手指与单指拖拽/点击）。</summary>
    private bool _pinchGestureSeen;
    /// <summary>起点时两指间距（像素；小于 <see cref="MinPinchSpan"/> 时不做比值缩放）。</summary>
    private double _pinchStartSpan;
    /// <summary>起点时的缩放值：目标缩放 = 起点缩放 × (当前间距 / 起点间距)，
    /// 每次都由绝对值算出，不累积误差，也不会因为夹到上下限而卡住。</summary>
    private double _pinchStartZoom = 1;
    /// <summary>双指手势期间的合并后中心点（屏幕坐标）。</summary>
    private Point _pinchLastCenter;

    /// <summary>两指间距低于该值（像素）时比值不稳定，退化为纯平移。</summary>
    private const double MinPinchSpan = 12;

    private string? _externalDropTargetId;
    private string? _touchGhostLabel;
    private Point? _touchGhostPosition;
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
        var props = e.GetCurrentPoint(this).Properties;
        RawPointerPositionForTest = e.GetPosition(null);

        // 触摸/手写笔没有「左键」概念（IsLeftButtonPressed 恒为 false），
        // 必须按指针类型放行，否则点座位/拖座位/平移在触屏上全部失效。
        var isTouch = e.Pointer.Type is PointerType.Touch or PointerType.Pen;
        if (!isTouch && !props.IsLeftButtonPressed) return;

        // 双指（触摸）→ 进入双指缩放，而不是把第二根手指丢掉：
        // 触摸设备上没有滚轮，双指捏合是唯一顺手的放大/缩小手势。
        var isSecondTouch = _activePointer is not null
            && !ReferenceEquals(_activePointer, e.Pointer)
            && _activePointer.Type == PointerType.Touch
            && e.Pointer.Type == PointerType.Touch;
        if (isSecondTouch)
        {
            if (TryStartPinch(e))
            {
                e.Handled = true;
                return;
            }

            // 同一双指组合已经识别过，第二根手指的后续按下/移动不再参与单指状态机
            if (_pinchActive)
            {
                e.Pointer.Capture(this);
                return;
            }
        }

        // 忽略其它指针（鼠标/笔的第二路输入），避免状态机错乱
        if (_activePointer is not null && !ReferenceEquals(_activePointer, e.Pointer))
            return;

        // 长按（Gestures.Holding）需要基类继续处理事件，这里不能再标记 Handled
        base.OnPointerPressed(e);

        // 上一轮手势没收到 Released（例如指针被系统吞掉）：这里清掉残留状态，
        // 否则画布会永久停在「拖动中」，下一次点击也不生效（触摸假死的表现）。
        if (_activePointer is not null)
            ResetPointerState();

        BeginGesture();
        _activePointer = e.Pointer;
        _isPointerPressed = true;

        Focus();
        _pointerPosition = e.GetPosition(this);
        _pressPosition = _pointerPosition;

        var seat = HitTestSeat(_pointerPosition);
        if (seat is not null && (IsSeatPicking || !seat.IsDisabled))
        {
            _dragSeatId = seat.Id;
            _dragTargetId = null;
            _isDragging = false;
            _dragBoardPosition = ToBoard(_pointerPosition);
            _dragGrabOffset = new Vector(_dragBoardPosition.X - seat.X, _dragBoardPosition.Y - seat.Y);
            if (!IsSeatPicking)
                SelectedSeatId = seat.Id;
            e.Pointer.Capture(this);
            return;
        }

        // 空白处：单指/鼠标平移，触摸端先保持惰性（不进入拖拽判定），
        // 见 OnPointerMoved 中对触摸空白的处理。
        _isPanning = true;
        _panLastPosition = new Vector(_pointerPosition.X, _pointerPosition.Y);
        // 触摸端没有鼠标指针，改游标只是白做，还可能残留成拖动形视觉
        if (!isTouch)
            Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        RawPointerPositionForTest = e.GetPosition(null);

        // 双指缩放：任何一根手指移动都要重新计算缩放与中心平移。
        // 必须放在写 _pointerPosition 之前——单指状态用的 _pointerPosition 一旦被第一根
        // 手指覆盖，两指间距就会算成 0，缩放会瞬间掉到最小再弹回（抖动/会场消失）。
        if (_pinchGestureSeen)
        {
            UpdatePinch(e.Pointer, e.GetPosition(this));
            return;
        }

        // 非当前手势指针（例如第一根手指按着、第二根被忽略时）的移动不能写 _pointerPosition，
        // 否则平移/拖拽会突然拿另一根手指的位置算增量，表现为画面跳一下或漂移。
        if (_activePointer is not null && !ReferenceEquals(e.Pointer, _activePointer))
            return;

        _pointerPosition = e.GetPosition(this);

        // 触摸/手写笔不维护悬停游标（没有悬停态），仅记录位置
        if (e.Pointer.Type is PointerType.Touch or PointerType.Pen && !_isPanning && _dragSeatId is null)
            return;

        if (_isPanning)
        {
            var current = new Vector(_pointerPosition.X, _pointerPosition.Y);
            var delta = current - _panLastPosition;
            _panLastPosition = current;

            // 触摸平移也走阈值：手指刚按下就抖一下不再移动板面，
            // 让「点选名单后点座位」的轻触不会把板面推走。
            if (e.Pointer.Type is PointerType.Touch or PointerType.Pen && !_isDragging)
            {
                if (Math.Abs(_pointerPosition.X - _pressPosition.X) <= DragThreshold &&
                    Math.Abs(_pointerPosition.Y - _pressPosition.Y) <= DragThreshold)
                {
                    return;
                }
                _isDragging = true;
            }

            PanBy(delta);
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

        if (!IsSeatPicking && !_isDragging &&
            (Math.Abs(_pointerPosition.X - _pressPosition.X) > DragThreshold ||
             Math.Abs(_pointerPosition.Y - _pressPosition.Y) > DragThreshold))
        {
            var seat = FindSeat(_dragSeatId);
            if (seat is null || seat.IsFixed)
            {
                return;
            }
            _isDragging = true;
            if (e.Pointer.Type is not (PointerType.Touch or PointerType.Pen))
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

        // 双指缩放：抬起任一参与的手指即结束手势（另一指继续按着也不再切换成拖拽）
        if (_pinchGestureSeen
            && (ReferenceEquals(e.Pointer, _pinchFirstPointer) || ReferenceEquals(e.Pointer, _activePointer)))
        {
            CancelPinch();
            _isPointerPressed = false;
            e.Pointer.Capture(null);
            EndGestureSoon();
            return;
        }

        // 先记录「已抬起」再分派事件：CompleteGesture 的 finally 会复位状态，
        // 而正常抬起时 Avalonia 也是先 Capture(null) 再派发 Released，
        // OnPointerCaptureLost 靠这个标志区分「正常抬起」与「真正被打断」。
        if (ReferenceEquals(_activePointer, e.Pointer))
            _isPointerPressed = false;

        if (!CompleteGesture(e.Pointer, e.GetPosition(this), isCancel: false))
            return;
        e.Pointer.Capture(null);
        EndGestureSoon();
    }

    /// <summary>
    /// 指针捕获丢失（其它控件抢走捕获、窗口失焦、系统手势接管等）。
    /// 必须清空拖拽/平移状态，否则 _dragSeatId 残留会让画布一直处于「拖动中」，
    /// 看起来就像界面假死（这是触摸拖动后卡住的根因之一）。
    /// </summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // 双指缩放期间任一根手指丢捕获：同样结束手势，避免缩放卡在半路
        if (_pinchGestureSeen
            && (ReferenceEquals(e.Pointer, _pinchFirstPointer) || ReferenceEquals(e.Pointer, _activePointer)))
        {
            CancelPinch();
            _isPointerPressed = false;
            EndGestureSoon();
            return;
        }

        if (_activePointer is null) return;

        // 只丢了一半：多指/触控板场景下另一根指针抢了捕获，但这一轮手势本身还在，
        // 此时若取消整轮手势，单指点击（按下→抬起之间可能短暂丢捕获）就会丢掉点击。
        if (!ReferenceEquals(_activePointer, e.Pointer)) return;

        // 正常「抬起」也会先释放捕获再收到 Released：此时指针已不再按下，
        // 说明手势已结束，直接复位即可，绝不能取消（否则点击/拖放都收不到）。
        if (!IsPointerDown(e.Pointer))
        {
            ResetPointerState();
            EndGestureSoon();
            return;
        }

        // 指针仍按下却丢了捕获 = 真正的打断，取消这一轮手势并复位状态。
        CompleteGesture(e.Pointer, _pointerPosition, isCancel: true);
        EndGestureSoon();
    }

    /// <summary>
    /// 该指针当前是否仍处于按下状态（鼠标按左键 / 触摸或手写笔接触屏幕）。
    /// Avalonia 12 没有 GetPointerPoint(IPointer) 这类查询 API，
    /// 因此由按下/抬起/取消三条路径自行维护 <see cref="_isPointerPressed"/>。
    /// </summary>
    private bool IsPointerDown(IPointer pointer)
        => ReferenceEquals(_activePointer, pointer) && _isPointerPressed;

    /// <summary>
    /// 结束一次手势：按当前状态分派「放下座位 / 点击座位」，然后无条件清空全部指针状态。
    /// <paramref name="isCancel"/> 为 true 时只清理不派发事件。
    /// </summary>
    /// <returns>是否需要调用方继续处理（例如释放指针捕获）。</returns>
    private bool CompleteGesture(IPointer pointer, Point position, bool isCancel)
    {
        if (!ReferenceEquals(_activePointer, pointer))
            return false;

        try
        {
            if (isCancel)
                return true;

            if (_isPanning)
                return true;

            if (_dragSeatId is { } seatId)
            {
                var seat = FindSeat(seatId);

                if (_isDragging && seat is not null)
                {
                    var target = HitTestSeat(position);
                    var targetId = target is not null
                        && !string.Equals(target.Id, seatId, StringComparison.Ordinal)
                        && !target.IsDisabled
                            ? target.Id
                            : null;

                    SeatDropped?.Invoke(this, new SeatDropEventArgs(seatId, targetId, seat.StudentId, position));
                }
                else
                {
                    // 拾取模式下超过阈值的拖动视为误触，不切换禁用状态
                    var moved = Math.Abs(position.X - _pressPosition.X) > DragThreshold
                        || Math.Abs(position.Y - _pressPosition.Y) > DragThreshold;
                    if (!IsSeatPicking || !moved)
                        SeatClicked?.Invoke(this, new SeatEventArgs(seatId, seat));
                }
            }

            return true;
        }
        finally
        {
            ResetPointerState();
        }
    }

    /// <summary>无条件清空拖拽/平移/指针状态（幂等，可安全重复调用）。</summary>
    private void ResetPointerState()
    {
        _dragSeatId = null;
        _dragTargetId = null;
        _isDragging = false;
        _isPanning = false;
        _dragBoardPosition = default;
        _dragGrabOffset = default;
        _activePointer = null;
        _isPointerPressed = false;
        _pinchFirstPointer = null;
        _pinchSecondPointer = null;
        _pinchSecondPosition = default;
        _pinchActive = false;
        _pinchGestureSeen = false;
        _pinchStartZoom = 1;
        Cursor = new Cursor(StandardCursorType.Arrow);
        InvalidateVisual();
    }

    // ─────────────── 双指缩放 ───────────────

    /// <summary>
    /// 第二根手指按下：识别为双指缩放手势，并放弃已经在进行的单指拖拽/平移。
    /// 触摸屏上没有滚轮，捏合缩放是唯一的缩放入口。
    /// </summary>
    private bool TryStartPinch(PointerPressedEventArgs e)
    {
        if (_activePointer is null) return false;

        // 已经在双指手势中：第三根手指既不能重新取基线，也不能参与计算，
        // 否则捏合会被「重启」，两指间距按错的两根手指算，画面同样会跳。
        if (_pinchGestureSeen) return false;

        // 第一根手指可能已经超过拖拽阈值（触摸抖动 4px 就会发生）：
        // 这里必须让捏合接管并取消拖拽，否则双指缩放会被误判为「拖动中」而直接失效。
        _pinchFirstPointer = _activePointer;
        _pinchFirstPosition = _pointerPosition;
        _pinchGestureSeen = true;

        // 丢掉单指阶段的拖拽/平移状态，防止捏合时座位跟着跑
        _dragSeatId = null;
        _dragTargetId = null;
        _isDragging = false;
        _isPanning = false;

        _activePointer = e.Pointer;
        _isPointerPressed = true;

        var second = e.GetPosition(this);
        _pinchSecondPointer = e.Pointer;
        _pinchSecondPosition = second;
        _pinchStartSpan = Distance(_pinchFirstPosition, second);
        _pinchStartZoom = Zoom;
        _pinchLastCenter = Midpoint(_pinchFirstPosition, second);
        _pinchActive = _pinchStartSpan >= MinPinchSpan;

        e.Pointer.Capture(this);
        BeginGesture();
        return true;
    }

    /// <summary>
    /// 双指移动：按两指间距的比例缩放（以两指中心为锚点），中心位移同时平移板面。
    /// 每根手指各记各的位置：任意一根移动都不能改写另一根的位置，
    /// 否则间距会瞬间归零，缩放随之跳变（表现为会场抽搐、甚至缩到看不见）。
    /// </summary>
    private void UpdatePinch(IPointer pointer, Point position)
    {
        if (ReferenceEquals(pointer, _pinchFirstPointer))
        {
            _pinchFirstPosition = position;
        }
        else if (ReferenceEquals(pointer, _pinchSecondPointer))
        {
            _pinchSecondPosition = position;
        }
        else
        {
            // 第三根手指或已结束指针的残留移动：不参与双指计算
            return;
        }

        var span = Distance(_pinchFirstPosition, _pinchSecondPosition);
        var center = Midpoint(_pinchFirstPosition, _pinchSecondPosition);

        if (!_pinchActive)
        {
            if (span < MinPinchSpan)
            {
                // 两指太近，比值不稳定：只跟随中心点平移
                PanBy(center - _pinchLastCenter);
                _pinchLastCenter = center;
                return;
            }

            _pinchStartSpan = span;
            _pinchStartZoom = Zoom;
            _pinchLastCenter = center;
            _pinchActive = true;
            InvalidateVisual();
            return;
        }

        // 间距塌缩（两指重合/事件丢帧）：重新取基线，绝不拿接近 0 的比值去缩放
        if (span < MinPinchSpan || _pinchStartSpan < MinPinchSpan)
        {
            _pinchStartSpan = Math.Max(span, MinPinchSpan);
            _pinchStartZoom = Zoom;
            _pinchLastCenter = center;
            return;
        }

        // 目标缩放由「起点缩放 × 间距比」算出，属于绝对值而不是逐帧增量：
        // 缩放上下限把目标夹住后，手指合拢时能立刻正确回落，不会卡在上限。
        var targetZoom = Math.Clamp(_pinchStartZoom * (span / _pinchStartSpan), MinZoom, MaxZoom);
        var delta = targetZoom / Zoom;
        if (Math.Abs(delta - 1) > 1e-4)
            ZoomAt(delta, center);

        PanBy(center - _pinchLastCenter);
        _pinchLastCenter = center;
    }

    /// <summary>双指手势结束（抬手 / 捕获丢失 / 被系统打断）：清空双指状态。</summary>
    private void CancelPinch()
    {
        _pinchFirstPointer = null;
        _pinchActive = false;
        _pinchGestureSeen = false;
        ResetPointerState();
    }

    private static double Distance(Point a, Point b)
        => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static Point Midpoint(Point a, Point b)
        => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

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
        public IPen AccentDashedPen { get; } = new Pen(Accent, 1.5, new DashStyle([4, 3], 0));
        public IPen DropTargetPen { get; } = new Pen(Accent, 2, new DashStyle([4, 3], 0));
        public IPen StalePen { get; } = new Pen(Warn, 2);
        public IPen OverlayPen { get; } = new Pen(BoardBorder, 1, new DashStyle([4, 4], 0));
        public IBrush SeatDisabledBg { get; } = Surface2;
        public IBrush FixedDot { get; } = Fixed;
        public IBrush GhostBg { get; } = AccentSoft;
    }
}
