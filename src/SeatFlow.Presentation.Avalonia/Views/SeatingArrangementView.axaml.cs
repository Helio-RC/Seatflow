using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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

    /// <summary>紧凑模式抽屉遮罩点击：关闭抽屉。</summary>
    private void DrawerMask_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SeatingArrangementViewModel vm)
            vm.CloseDrawersCommand.Execute(null);
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

    /// <summary>点击百分比：重置为设置中的默认缩放（关联「座位图默认缩放」）。</summary>
    private void ResetZoom_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SeatingArrangementViewModel vm)
            SeatCanvas.Zoom = vm.DefaultZoomLevel;
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

    // ── 未分配列表拖动（系统 DnD → 画布命中） ──

    private async void UnassignedStudent_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border
            || border.DataContext is not Core.Models.Student student)
            return;

        var data = new DataTransfer();
        var studentItem = new DataTransferItem();
        studentItem.Set(DragFormats.StudentDrag, student.Id);
        data.Add(studentItem);

        var result = await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);

        // 如果没有发生拖放，手动设置选中项（因为 DoDragDropAsync 阻止了 ListBox 的默认选择行为）
        if (result == DragDropEffects.None
            && DataContext is SeatingArrangementViewModel vm)
        {
            vm.SelectedUnassignedStudent = student;
        }
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
