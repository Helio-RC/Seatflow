using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace A_Pair.Presentation.Avalonia.Views;

public partial class SeatingArrangementView : UserControl
{
    public SeatingArrangementView ()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded (object? sender , RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SeatingArrangementViewModel vm)
            _ = vm.RefreshDataAsync();
    }

    // ── 拖放数据格式 ──

    internal static class DragFormats
    {
        public static readonly DataFormat<string> StudentDrag = DataFormat.CreateInProcessFormat<string>("A_Pair_Student");
        public static readonly DataFormat<string> SeatDrag = DataFormat.CreateInProcessFormat<string>("A_Pair_Seat");
    }

    // ── 拖放辅助方法 ──

    private static bool DragHasFormat (IDataTransfer transfer , DataFormat format)
        => transfer.Items.Any(item => item.Formats.Contains(format));

    private static string? DragGetString (IDataTransfer transfer , DataFormat<string> format)
    {
        foreach (var item in transfer.Items)
        {
            if (item.Formats.Contains(format))
                return item.TryGetRaw(format) is string s ? s : null;
        }
        return null;
    }

    // ── 座位拖拽检测状态 ──

    private Point? _seatDragStart;
    private PointerPressedEventArgs? _seatDragPressArgs;
    private ViewModels.SeatDisplayItem? _seatDragItem;
    private bool _seatWasDrag;
    private static readonly double DragThreshold = 5.0;

    // ── 未分配列表拖拽检测状态 ──

    private Point? _listDragStart;
    private PointerPressedEventArgs? _listDragPressArgs;
    private Core.Models.Student? _listDragStudent;

    // ── 座位点击/拖拽 ──

    private void SeatBorder_PointerPressed (object? sender , PointerPressedEventArgs e)
    {
        if (sender is not Border border
            || border.DataContext is not ViewModels.SeatDisplayItem item)
            return;

        var pos = e.GetPosition(this);
        _seatDragStart = pos;
        _seatDragPressArgs = e;
        _seatDragItem = item;
        _seatWasDrag = false;
        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private async void SeatBorder_PointerMoved (object? sender , PointerEventArgs e)
    {
        if (_seatDragStart == null || _seatDragItem == null || _seatDragPressArgs == null) return;

        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) return;

        var pos = e.GetPosition(this);
        double dx = pos.X - _seatDragStart.Value.X;
        double dy = pos.Y - _seatDragStart.Value.Y;

        if (Math.Sqrt(dx * dx + dy * dy) <= DragThreshold) return;

        if (!_seatDragItem.IsOccupied || _seatDragItem.IsFixed) return;
        if (_seatDragItem.StudentId == null) return;

        var item = _seatDragItem;
        var pressArgs = _seatDragPressArgs;
        _seatWasDrag = true;

        var data = new DataTransfer();
        var studentItem = new DataTransferItem();
        studentItem.Set(DragFormats.StudentDrag , item.StudentId);
        data.Add(studentItem);
        var seatItem = new DataTransferItem();
        seatItem.Set(DragFormats.SeatDrag , item.SeatId);
        data.Add(seatItem);

        await DragDrop.DoDragDropAsync(pressArgs , data , DragDropEffects.Move);

        _seatDragStart = null;
        _seatDragPressArgs = null;
        _seatDragItem = null;
    }

    private void SeatBorder_PointerReleased (object? sender , PointerReleasedEventArgs e)
    {
        if (_seatWasDrag || _seatDragItem == null) return;

        if (DataContext is ViewModels.SeatingArrangementViewModel vm)
            vm.ClickSeatCommand.Execute(_seatDragItem);

        _seatDragStart = null;
        _seatDragPressArgs = null;
        _seatDragItem = null;
    }

    // ── 未分配列表拖动 ──

    private void UnassignedStudent_PointerPressed (object? sender , PointerPressedEventArgs e)
    {
        if (sender is not TextBlock tb
            || tb.DataContext is not Core.Models.Student student)
            return;

        _listDragStart = e.GetPosition(this);
        _listDragPressArgs = e;
        _listDragStudent = student;
        e.Pointer.Capture(tb);
    }

    private async void UnassignedStudent_PointerMoved (object? sender , PointerEventArgs e)
    {
        if (_listDragStart == null || _listDragStudent == null || _listDragPressArgs == null) return;

        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) return;

        var pos = e.GetPosition(this);
        double dx = pos.X - _listDragStart.Value.X;
        double dy = pos.Y - _listDragStart.Value.Y;

        if (Math.Sqrt(dx * dx + dy * dy) <= DragThreshold) return;

        var student = _listDragStudent;
        var pressArgs = _listDragPressArgs;
        _listDragStart = null;
        _listDragPressArgs = null;
        _listDragStudent = null;

        var data = new DataTransfer();
        var item = new DataTransferItem();
        item.Set(DragFormats.StudentDrag , student.Id);
        data.Add(item);

        await DragDrop.DoDragDropAsync(pressArgs , data , DragDropEffects.Move);
    }

    private void UnassignedStudent_PointerReleased (object? sender , PointerReleasedEventArgs e)
    {
        _listDragStart = null;
        _listDragPressArgs = null;
        _listDragStudent = null;
    }

    // ── 座位放置目标 ──

    private void Seat_DragOver (object? sender , DragEventArgs e)
    {
        e.Handled = true;

        if (sender is not Border border
            || border.DataContext is not ViewModels.SeatDisplayItem seat)
            return;

        if (seat.IsFixed) { e.DragEffects = DragDropEffects.None; return; }

        var transfer = e.DataTransfer;
        bool hasStudent = DragHasFormat(transfer , DragFormats.StudentDrag);
        bool hasSeat = DragHasFormat(transfer , DragFormats.SeatDrag);

        if (seat.IsOccupied && !hasSeat)
        { e.DragEffects = DragDropEffects.None; return; }

        if (hasSeat)
        {
            var srcSeatId = DragGetString(transfer , DragFormats.SeatDrag);
            if (srcSeatId == seat.SeatId)
            { e.DragEffects = DragDropEffects.None; return; }
        }

        seat.IsDragHover = true;
        e.DragEffects = DragDropEffects.Move;
    }

    private async void Seat_Drop (object? sender , DragEventArgs e)
    {
        if (sender is not Border border
            || border.DataContext is not ViewModels.SeatDisplayItem seat
            || DataContext is not ViewModels.SeatingArrangementViewModel vm)
            return;

        seat.IsDragHover = false;

        var transfer = e.DataTransfer;
        var studentId = DragGetString(transfer , DragFormats.StudentDrag);
        var sourceSeatId = DragGetString(transfer , DragFormats.SeatDrag);
        if (string.IsNullOrEmpty(studentId)) return;

        await vm.ExecuteDropAsync(studentId , sourceSeatId , seat.SeatId);
    }

    private void Seat_DragLeave (object? sender , DragEventArgs e)
    {
        if (sender is Border border
            && border.DataContext is ViewModels.SeatDisplayItem seat)
            seat.IsDragHover = false;
    }

    // ── 垃圾桶 ──

    private IBrush? _trashOriginalBg;

    private void Trash_DragOver (object? sender , DragEventArgs e)
    {
        bool hasSeat = DragHasFormat(e.DataTransfer , DragFormats.SeatDrag);
        e.DragEffects = hasSeat ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;

        if (sender is not Border trashBorder) return;

        _trashOriginalBg ??= trashBorder.Background;
        trashBorder.Background = new SolidColorBrush(Color.FromArgb(0x40 , 0xE8 , 0x11 , 0x23));
    }

    private void RestoreTrashBackground (object? sender)
    {
        if (sender is Border trashBorder && _trashOriginalBg != null)
        {
            trashBorder.Background = _trashOriginalBg;
            _trashOriginalBg = null;
        }
    }

    private async void Trash_Drop (object? sender , DragEventArgs e)
    {
        RestoreTrashBackground(sender);

        var seatId = DragGetString(e.DataTransfer , DragFormats.SeatDrag);
        if (string.IsNullOrEmpty(seatId)) return;

        if (DataContext is ViewModels.SeatingArrangementViewModel vm)
            await vm.ExecuteRemoveToTrashAsync(seatId);
    }

    private void Trash_DragLeave (object? sender , DragEventArgs e)
    {
        RestoreTrashBackground(sender);
    }

    private async void Trash_PointerPressed (object? sender , PointerPressedEventArgs e)
    {
        if (DataContext is ViewModels.SeatingArrangementViewModel vm)
            await vm.RemoveToTrashCommand.ExecuteAsync(null);
    }
}
