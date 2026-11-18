using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using SeatFlow.Presentation.Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Views;

/// <summary>
/// 排座工作台视图（M3：三栏 + 页签检查器）。
/// 说明：当前 <c>NavigationService</c> 尚未调用 <c>IPageLifecycle</c>，
/// 因此在 Loaded/Unloaded 桥接页面生命周期（进入时加载、离开时取消在途任务）。
/// </summary>
public partial class SeatingArrangementView : UserControl
{
    private CancellationTokenSource? _lifecycleCts;

    public SeatingArrangementView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _lifecycleCts?.Cancel();
        _lifecycleCts?.Dispose();
        _lifecycleCts = new CancellationTokenSource();

        if (DataContext is SeatingArrangementViewModel vm)
            _ = vm.OnEnterAsync(_lifecycleCts.Token);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _lifecycleCts?.Cancel();
        _lifecycleCts?.Dispose();
        _lifecycleCts = null;

        if (DataContext is SeatingArrangementViewModel vm)
            _ = vm.OnLeaveAsync();
    }

    /// <summary>紧凑档抽屉遮罩点击：关闭抽屉。</summary>
    private void DrawerMask_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SeatingArrangementViewModel vm)
            vm.CloseDrawersCommand.Execute(null);
    }

    /// <summary>长按生成按钮（按住达到阈值）：进入连续生成会话。</summary>
    private void GenerateButton_HoldStarted(object? sender, EventArgs e)
    {
        if (DataContext is SeatingArrangementViewModel vm)
            _ = vm.StartContinuousGenerateAsync();
    }

    /// <summary>松开 / 指针捕获丢失：结束连续生成会话（在途生成跑完即停）。</summary>
    private void GenerateButton_HoldEnded(object? sender, EventArgs e)
    {
        if (DataContext is SeatingArrangementViewModel vm)
            vm.StopContinuousGenerate();
    }

    // ── 拖放数据格式（自绘座位的内部拖拽不再走系统 DnD；此处用于「未分配学生 → 画布」） ──

    internal static class DragFormats
    {
        public static readonly DataFormat<string> StudentDrag = DataFormat.CreateInProcessFormat<string>("SeatFlow_Student");
        public static readonly DataFormat<string> SeatDrag = DataFormat.CreateInProcessFormat<string>("SeatFlow_Seat");
    }

    private static bool DragHasFormat(IDataTransfer transfer, DataFormat format)
        => transfer.Formats.Contains(format);

    private static string? DragGetString(IDataTransfer transfer, DataFormat format)
    {
        foreach (var item in transfer.Items)
        {
            if (item.Formats.Contains(format))
                return item.TryGetRaw(format) is string s ? s : null;
        }
        return null;
    }

    // ── 缩放控制条（作用与滚轮缩放一致，以视口中心为锚点） ──

    private void ZoomOut_Click(object? sender, RoutedEventArgs e)
        => ApplyZoomStep(1 / 1.1);

    private void ZoomIn_Click(object? sender, RoutedEventArgs e)
        => ApplyZoomStep(1.1);

    private void ApplyZoomStep(double factor)
    {
        var center = new Point(SeatCanvas.Bounds.Width / 2, SeatCanvas.Bounds.Height / 2);
        SeatCanvas.ZoomAt(factor, center);
    }

    /// <summary>点击百分比：重置为设置中的默认缩放，并清掉平移量（回到初始视图）。</summary>
    private void ResetZoom_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SeatingArrangementViewModel vm)
            SeatCanvas.ResetView(vm.DefaultZoomLevel);
    }

    // ── 座位点击 / 键盘激活 ──

    private void SeatCanvas_SeatClicked(object? sender, SeatEventArgs e)
        => ExecuteSeatClick(e.SeatId);

    private void SeatCanvas_SeatActivated(object? sender, SeatEventArgs e)
        => ExecuteSeatClick(e.SeatId);

    private void ExecuteSeatClick(string seatId)
    {
        if (DataContext is not SeatingArrangementViewModel vm) return;
        var item = vm.SeatItems.FirstOrDefault(s => s.SeatId == seatId);
        if (item is not null)
            vm.ClickSeatCommand.Execute(item);
    }

    // ── 画布内部拖拽放下（座位↔座位 / 座位→空白；空白处判定垃圾桶） ──

    private async void SeatCanvas_SeatDropped(object? sender, SeatDropEventArgs e)
    {
        if (DataContext is not SeatingArrangementViewModel vm) return;
        if (string.IsNullOrEmpty(e.StudentId)) return;

        if (!string.IsNullOrEmpty(e.TargetSeatId))
        {
            await vm.ExecuteDropAsync(e.StudentId, e.SourceSeatId, e.TargetSeatId);
            return;
        }

        // 落在空白：若指针位于垃圾桶范围内 → 移除到回收站
        if (IsPointerOverTrash(e.PointerPosition))
            await vm.ExecuteRemoveToTrashAsync(e.SourceSeatId);
    }

    private bool IsPointerOverTrash(Point pointerInCanvas)
    {
        var pagePoint = SeatCanvas.TranslatePoint(pointerInCanvas, this);
        var trashTopLeft = TrashZone.TranslatePoint(default, this);
        if (pagePoint is not { } p || trashTopLeft is not { } tl) return false;
        return new Rect(tl, TrashZone.Bounds.Size).Contains(p);
    }

    // ── 未分配列表拖动（鼠标走系统 DnD；触摸/手写笔走按下捕获 + 指针跟踪的「自己去拖」） ──

    private async void UnassignedStudent_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border
            || border.DataContext is not Core.Models.Student student)
            return;

        // 触摸/手写笔没有「左键」概念：若在此 await DoDragDropAsync，
        // 触摸指针仍被列表捕获、DragOver/Drop 不会触发、await 永不返回 → 界面假死。
        // 因此触摸改为：选中该学生 + 由列表自己跟踪手指，拖到画布上再落座。
        if (e.Pointer.Type is PointerType.Touch or PointerType.Pen)
        {
            if (DataContext is SeatingArrangementViewModel touchVm)
                touchVm.SelectedUnassignedStudent = student;

            _touchDragStudent = student;
            _touchDragStarted = false;
            _touchDragPointer = e.Pointer;
            _touchDragBorder = border;
            _touchDragStart = e.GetPosition(border);
            TouchDragEventCountForTest = 0;

            // 指针事件一律挂在画布所在的层上（父级捕获，能收到子元素事件）：
            // 未分配列表在另一个子树上、滚出画布区域后指针事件不会回到被捕获的 Border，
            // 所以监听必须放在两棵树共同的祖先上，才能跨栏跟踪到座位。
            this.RemoveHandler(PointerMovedEvent, (EventHandler<PointerEventArgs>)OnTouchDragPointerMoved);
            this.RemoveHandler(PointerReleasedEvent, (EventHandler<PointerReleasedEventArgs>)OnTouchDragPointerReleased);
            this.RemoveHandler(PointerCaptureLostEvent, (EventHandler<PointerCaptureLostEventArgs>)OnTouchDragPointerCaptureLost);
            this.AddHandler(PointerMovedEvent, (EventHandler<PointerEventArgs>)OnTouchDragPointerMoved);
            this.AddHandler(PointerReleasedEvent, (EventHandler<PointerReleasedEventArgs>)OnTouchDragPointerReleased);
            this.AddHandler(PointerCaptureLostEvent, (EventHandler<PointerCaptureLostEventArgs>)OnTouchDragPointerCaptureLost);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            return;

        var data = new DataTransfer();
        var studentItem = new DataTransferItem();
        studentItem.Set(DragFormats.StudentDrag, student.Id);
        data.Add(studentItem);

        DragDropEffects result;
        try
        {
            result = await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        catch (Exception)
        {
            // 捕获被抢占等异常不应让列表停在拖拽状态
            result = DragDropEffects.None;
        }

        // 如果没有发生拖放，手动设置选中项（因为 DoDragDropAsync 阻止了 ListBox 的默认选择行为）
        if (result == DragDropEffects.None
            && DataContext is SeatingArrangementViewModel vm)
        {
            vm.SelectedUnassignedStudent = student;
        }
    }

    // ── 触摸拖名字：列表捕获指针，移到画布上时由画布报告目标座位 ──

    private Core.Models.Student? _touchDragStudent;
    private bool _touchDragStarted;
    private IPointer? _touchDragPointer;
    private Point _touchDragStart;
    private Visual? _touchDragBorder;




    private void OnTouchDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_touchDragStudent is null || !ReferenceEquals(e.Pointer, _touchDragPointer))
            return;

        // 指针已被本视图捕获：坐标按本视图（与画布同一坐标系）计算，
        // 指针移出列表后仍能准确落到底下的座位上。
        var position = e.GetPosition(this);
        TouchDragEventCountForTest++;
        if (!_touchDragStarted)
        {
            if (Math.Abs(position.X - _touchDragStart.X) <= TouchDragThreshold
                && Math.Abs(position.Y - _touchDragStart.Y) <= TouchDragThreshold)
            {
                return;
            }

            _touchDragStarted = true;
        }

        UpdateTouchDragTarget(position, null);
    }


    private void OnTouchDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _touchDragPointer)) return;

        var dropped = _touchDragStarted;
        var student = _touchDragStudent;
        var position = e.GetPosition(this);
        var seat = dropped ? HitTestSeatOnCanvas(position, null) : null;
        TouchDragEventCountForTest++;

        EndTouchDrag(e.Pointer);

        // 松开在可放置的座位上 → 落座；否则回到点选（用户随后点座位即可放置）
        if (dropped && student is not null && seat is not null
            && DataContext is SeatingArrangementViewModel vm)
        {
            _ = vm.ExecuteDropAsync(student.Id, null, seat.Id);
        }
    }


    private void OnTouchDragPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(e.Pointer, _touchDragPointer))
            EndTouchDrag(null);
    }

    /// <summary>指针位于画布上方时上报目标座位并画幽灵名字；离开画布则清除高亮。</summary>
    private void UpdateTouchDragTarget(Point position, Visual? coordinateSource)
    {
        var seat = HitTestSeatOnCanvas(position, coordinateSource);
        var canvasPoint = TranslateToCanvas(position, coordinateSource);
        SeatCanvas.SetDragGhost(_touchDragStudent?.Name, canvasPoint);
        SeatCanvas.SetDropTarget(seat is { IsDisabled: false, IsFixed: false } ? seat.Id : null);
    }

    private Point? TranslateToCanvas(Point position, Visual? coordinateSource)
    {
        // 指针由本视图捕获时 position 是「视图坐标」；画布是视图里的子控件，
        // 必须再减去画布相对视图的偏移才是画布坐标。鼠标 DnD 传入的元素坐标也走同一条路。
        var inThis = coordinateSource is null || ReferenceEquals(coordinateSource, this)
            ? position
            : TranslateFrom(coordinateSource, position, this);
        var offset = SeatCanvas.TranslatePoint(default, this);
        return offset is { } origin
            ? new Point(inThis.X - origin.X, inThis.Y - origin.Y)
            : inThis;
    }

    private SeatVisual? HitTestSeatOnCanvas(Point position, Visual? coordinateSource)
    {
        var canvasPoint = TranslateToCanvas(position, coordinateSource);
        if (canvasPoint is not { } point)
            return null;
        if (!new Rect(SeatCanvas.Bounds.Size).Contains(point))
            return null;
        return SeatCanvas.HitTestSeat(point);
    }

    /// <summary>把 <paramref name="from"/> 坐标系中的点换算到 <paramref name="to"/> 坐标系。</summary>
    private static Point TranslateFrom(Visual from, Point point, Visual to)
        => from.TranslatePoint(point, to) ?? point;

    private void EndTouchDrag(IPointer? pointer)
    {
        _touchDragStudent = null;
        _touchDragStarted = false;
        _touchDragPointer = null;
        _touchDragBorder = null;
        SeatCanvas.SetDragGhost(null, null);
        SeatCanvas.SetDropTarget(null);
        pointer?.Capture(null);
    }

    /// <summary>触摸拖名字的启动阈值（页坐标像素）。</summary>
    private const double TouchDragThreshold = 8;

    /// <summary>当前正在触摸拖拽的学生姓名（测试/诊断用）。</summary>
    internal string? TouchDragStudentNameForTest => _touchDragStudent?.Name;

    /// <summary>触摸拖拽是否已越过启动阈值（测试/诊断用）。</summary>
    internal bool TouchDragActiveForTest => _touchDragStarted;

    /// <summary>触摸拖拽收到指针事件的次数（测试用）。</summary>
    internal int TouchDragEventCountForTest { get; private set; }

    /// <summary>
    /// 画布长按（触摸拖起的替代路径）：把当前选中的未分配学生放入长按命中的座位。
    /// 触摸设备上系统 DnD 不可靠，长按是最稳的「拖过去」手势。
    /// </summary>
    private async void SeatCanvas_Holding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started) return;
        if (DataContext is not SeatingArrangementViewModel vm) return;

        var student = vm.SelectedUnassignedStudent;
        if (student is null) return;

        var seat = SeatCanvas.HitTestSeat(e.Position);
        if (seat is null || seat.IsDisabled || seat.IsFixed || seat.IsOccupied) return;

        e.Handled = true;
        await vm.ExecuteDropAsync(student.Id, null, seat.Id);
    }

    // ── 画布作为外部拖放目标 ──

    private void SeatCanvas_DragOver(object? sender, DragEventArgs e)
    {
        var transfer = e.DataTransfer;
        bool hasStudent = DragHasFormat(transfer, DragFormats.StudentDrag);
        var seat = SeatCanvas.HitTestSeat(e.GetPosition(SeatCanvas));

        if (!hasStudent || seat is null || seat.IsDisabled)
        {
            SeatCanvas.SetDropTarget(null);
            e.DragEffects = DragDropEffects.None;
            return;
        }

        bool hasSeat = DragHasFormat(transfer, DragFormats.SeatDrag);
        if (seat.IsFixed || (seat.IsOccupied && !hasSeat))
        {
            SeatCanvas.SetDropTarget(null);
            e.DragEffects = DragDropEffects.None;
            return;
        }

        if (hasSeat && DragGetString(transfer, DragFormats.SeatDrag) == seat.Id)
        {
            SeatCanvas.SetDropTarget(null);
            e.DragEffects = DragDropEffects.None;
            return;
        }

        SeatCanvas.SetDropTarget(seat.Id);
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void SeatCanvas_DragLeave(object? sender, DragEventArgs e)
        => SeatCanvas.SetDropTarget(null);

    private async void SeatCanvas_Drop(object? sender, DragEventArgs e)
    {
        SeatCanvas.SetDropTarget(null);

        if (DataContext is not SeatingArrangementViewModel vm) return;

        var seat = SeatCanvas.HitTestSeat(e.GetPosition(SeatCanvas));
        if (seat is null || seat.IsDisabled || seat.IsFixed) return;

        var studentId = DragGetString(e.DataTransfer, DragFormats.StudentDrag);
        if (string.IsNullOrEmpty(studentId)) return;

        // 座位内部拖拽不经过系统 DnD；这里只可能出现未分配学生
        await vm.ExecuteDropAsync(studentId, null, seat.Id);
    }

    // ── 垃圾桶（系统 DnD：未分配学生拖入无效；保留视觉反馈，点击移除选中座位） ──

    private IBrush? _trashOriginalBg;

    private void Trash_DragOver(object? sender, DragEventArgs e)
    {
        bool hasSeat = DragHasFormat(e.DataTransfer, DragFormats.SeatDrag);
        e.DragEffects = hasSeat ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;

        if (sender is not Border trashBorder) return;

        _trashOriginalBg ??= trashBorder.Background;
        trashBorder.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xE8, 0x11, 0x23));
    }

    private void RestoreTrashBackground(object? sender)
    {
        if (sender is Border trashBorder && _trashOriginalBg != null)
        {
            trashBorder.Background = _trashOriginalBg;
            _trashOriginalBg = null;
        }
    }

    private async void Trash_Drop(object? sender, DragEventArgs e)
    {
        RestoreTrashBackground(sender);

        var seatId = DragGetString(e.DataTransfer, DragFormats.SeatDrag);
        if (string.IsNullOrEmpty(seatId)) return;

        if (DataContext is SeatingArrangementViewModel vm)
            await vm.ExecuteRemoveToTrashAsync(seatId);
    }

    private void Trash_DragLeave(object? sender, DragEventArgs e)
    {
        RestoreTrashBackground(sender);
    }

    private async void Trash_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SeatingArrangementViewModel vm)
            await vm.RemoveToTrashCommand.ExecuteAsync(null);
    }
}
