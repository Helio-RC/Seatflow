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

    // ── 座位点击/拖拽 ──

    private async void SeatBorder_PointerPressed (object? sender , PointerPressedEventArgs e)
    {
        if (sender is not Border border
            || border.DataContext is not ViewModels.SeatDisplayItem item)
            return;

        var vm = DataContext as ViewModels.SeatingArrangementViewModel;

        // 如果座位已占用且可以拖拽，启动拖放操作
        if (item.IsOccupied && !item.IsFixed && item.StudentId != null)
        {
            var data = new DataTransfer();
            var studentData = new DataTransferItem();
            studentData.Set(DragFormats.StudentDrag , item.StudentId);
            data.Add(studentData);
            var seatData = new DataTransferItem();
            seatData.Set(DragFormats.SeatDrag , item.SeatId);
            data.Add(seatData);

            var result = await DragDrop.DoDragDropAsync(e , data , DragDropEffects.Move);

            // 如果没有发生实际拖放（点击行为），执行原有的点击逻辑
            if (result == DragDropEffects.None && vm != null)
                vm.ClickSeatCommand.Execute(item);
        }
        else
        {
            // 空座位或无权限座位：直接执行点击逻辑
            vm?.ClickSeatCommand.Execute(item);
        }
    }

    // ── 未分配列表拖动 ──

    private async void UnassignedStudent_PointerPressed (object? sender , PointerPressedEventArgs e)
    {
        if (sender is not TextBlock tb
            || tb.DataContext is not Core.Models.Student student)
            return;

        var data = new DataTransfer();
        var item = new DataTransferItem();
        item.Set(DragFormats.StudentDrag , student.Id);
        data.Add(item);

        await DragDrop.DoDragDropAsync(e , data , DragDropEffects.Move);
    }

    // ── 座位放置目标 ──

    private void Seat_DragOver (object? sender , DragEventArgs e)
    {
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
        e.Handled = true;
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
