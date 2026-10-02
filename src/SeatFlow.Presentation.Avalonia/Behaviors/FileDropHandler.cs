using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using FluentIcons.Common;
using AvaloniaApp = Avalonia.Application;

namespace SeatFlow.Presentation.Avalonia.Behaviors;

/// <summary>
/// 全局文件拖放导入服务（DI 单例）。注册在 <see cref="Views.MainView"/> 上，拦截 OS 文件拖放事件，
/// 根据当前页面的 ViewModel 是否实现 <see cref="IFileDropHandler"/> 来路由处理。
/// 拖入文件时显示遮罩覆盖层（支持/不支持两种状态）。
/// M0 起：由静态 <c>_host</c> + 服务定位改为 DI 单例（注入导航与对话框服务）。
/// </summary>
public sealed class FileDropHandler
{
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialog;
    private Control? _host;

    public FileDropHandler(INavigationService navigation, IDialogService dialog)
    {
        _navigation = navigation;
        _dialog = dialog;
    }

    public void Attach(Control host)
    {
        _host = host;
        DragDrop.AddDragOverHandler(host, OnDragOver);
        DragDrop.AddDropHandler(host, OnDrop);
        DragDrop.AddDragEnterHandler(host, OnDragEnter);
        DragDrop.AddDragLeaveHandler(host, OnDragLeave);
    }

    private ViewModelBase? ResolveCurrentViewModel() => _navigation.CurrentViewModel;

    private static string[]? GetDroppedFilePaths(DragEventArgs e)
    {
        if (!e.DataTransfer.Formats.Contains(DataFormat.File))
            return null;

        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
            return null;

        return files.Select(f => f.Path.LocalPath).ToArray();
    }

    /// <summary>判断当前拖入的文件是否被当前页面接受。</summary>
    private bool IsAcceptedByCurrentPage(DragEventArgs e)
    {
        var vm = ResolveCurrentViewModel();
        if (vm is not IFileDropHandler handler)
            return false;

        if (!e.DataTransfer.Formats.Contains(DataFormat.File))
            return false;

        var filePaths = GetDroppedFilePaths(e);
        if (filePaths is null || filePaths.Length == 0 || filePaths.Length > 1)
            return false;

        var ext = Path.GetExtension(filePaths[0])?.ToLowerInvariant();
        return ext is not null && handler.AcceptedFileExtensions.Contains(ext);
    }

    /// <summary>显示遮罩覆盖层并根据页面对文件的接受情况设置图标、文字和边框颜色。</summary>
    private void SetOverlayState(bool accepted)
    {
        if (_host is null) return;

        var overlay = _host.FindControl<Border>("FileDropOverlay");
        if (overlay is null) return;

        var icon = _host.FindControl<FluentIcons.Avalonia.FluentIcon>("FileDropOverlayIcon");
        var text = _host.FindControl<TextBlock>("FileDropOverlayText");
        var card = _host.FindControl<Border>("FileDropOverlayCard");

        // 使用 Application 级别的资源查找（Window.FindResource 找不到主题级资源）
        var accentBrush = AvaloniaApp.Current?.FindResource("SystemAccentColor") as IBrush;
        var errorBrush = AvaloniaApp.Current?.FindResource("SystemFillColorCriticalBrush") as IBrush;

        if (accepted)
        {
            if (icon is not null) icon.Icon = Icon.ArrowDownload;
            if (text is not null) text.Text = Resources.DragDrop_DropHint;
            if (card is not null && accentBrush is not null) card.BorderBrush = accentBrush;
        }
        else
        {
            if (icon is not null) icon.Icon = Icon.Dismiss;
            if (text is not null) text.Text = Resources.DragDrop_UnsupportedDropHint;
            if (card is not null && errorBrush is not null) card.BorderBrush = errorBrush;
        }

        overlay.IsVisible = true;
    }

    private void HideOverlay()
    {
        if (_host is null) return;
        var overlay = _host.FindControl<Border>("FileDropOverlay");
        if (overlay is not null)
            overlay.IsVisible = false;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Formats.Contains(DataFormat.File))
            return;

        SetOverlayState(IsAcceptedByCurrentPage(e));
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        HideOverlay();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var vm = ResolveCurrentViewModel();
        if (vm is not IFileDropHandler handler)
        {
            if (e.DataTransfer.Formats.Contains(DataFormat.File))
                e.DragEffects = DragDropEffects.None;
            return;
        }

        var filePaths = GetDroppedFilePaths(e);
        if (filePaths is null || filePaths.Length == 0)
            return;

        if (filePaths.Length > 1)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var ext = Path.GetExtension(filePaths[0])?.ToLowerInvariant();
        if (ext is not null && handler.AcceptedFileExtensions.Contains(ext))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        HideOverlay();

        try
        {
            var filePaths = GetDroppedFilePaths(e);
            if (filePaths is null || filePaths.Length == 0)
                return;

            var vm = ResolveCurrentViewModel();

            if (filePaths.Length > 1)
            {
                var confirmed = await _dialog.ShowConfirmAsync(
                    Resources.DragDrop_MultipleFilesTitle,
                    string.Format(Resources.DragDrop_MultipleFilesMsg, filePaths.Length));
                if (!confirmed) return;
            }

            var filePath = filePaths[0];
            var ext = Path.GetExtension(filePath)?.ToLowerInvariant();

            if (vm is IFileDropHandler handler)
            {
                if (ext is null || !handler.AcceptedFileExtensions.Contains(ext))
                {
                    await _dialog.ShowWarningAsync(
                        Resources.DragDrop_InvalidFileType,
                        string.Format(Resources.DragDrop_InvalidFileTypeFmt,
                            Path.GetFileName(filePath),
                            string.Join(", ", handler.AcceptedFileExtensions)));
                    return;
                }

                await handler.HandleFileDropAsync([filePath], CancellationToken.None);
            }
            else
            {
                await _dialog.ShowInfoAsync(
                    Resources.DragDrop_UnsupportedPage,
                    Resources.DragDrop_UnsupportedPageMsg);
            }
        }
        catch (Exception ex)
        {
            try
            {
                await _dialog.ShowErrorAsync(Resources.Common_OperationFailed, ex.Message);
            }
            catch { /* 弹窗不可用时静默处理 */ }
        }
    }
}
