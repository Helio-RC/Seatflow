using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SeatFlow.Core.Models.SeatSets;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Presentation.Avalonia.Views;

namespace SeatFlow.Presentation.Avalonia.Services.Web;

/// <summary>
/// 浏览器端（WASM）对话框服务：使用 <see cref="MainView"/> 的 <c>DialogOverlayHost</c>
/// 遮罩 + 居中面板模拟模态对话框（WASM 不支持独立 Window / ShowDialog）。
/// </summary>
public sealed class WebDialogService : IDialogService
{
    private TopLevel? _topLevel;

    public void SetTopLevel(TopLevel topLevel) => _topLevel = topLevel;

    private async Task<int?> ShowCoreAsync(string title, string message, DialogKind kind,
        string? button1 = null, string? button2 = null, string? button3 = null)
    {
        var host = await ResolveHostAsync();
        if (host is null) return null;

        var tcs = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var content = new DialogContent
        {
            DialogTitle = title,
            Message = message,
            Kind = kind,
            Button1Text = button1,
            Button2Text = button2,
            Button3Text = button3
        };
        content.Completed += _ => tcs.TrySetResult(content.DialogResult);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            host.DialogOverlayContent.Content = WrapOverlay(content, host, 420, 480);
            host.DialogOverlayHost.IsVisible = true;
            content.OnContentAttached();
        });

        var result = await tcs.Task;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            host.DialogOverlayHost.IsVisible = false;
            host.DialogOverlayContent.Content = null;
        });
        return result;
    }

    public Task ShowErrorAsync(string title, string message)
        => ShowAsync(title, message, DialogKind.Error);

    public Task ShowWarningAsync(string title, string message)
        => ShowAsync(title, message, DialogKind.Warning);

    public Task ShowInfoAsync(string title, string message)
        => ShowAsync(title, message, DialogKind.Info);

    public async Task<bool> ShowConfirmAsync(string title, string message)
        => await ShowCoreAsync(title, message, DialogKind.Confirm) is 0;

    public async Task<int?> ShowMultiOptionAsync(string title, string message,
        string primaryText, string secondaryText, string? cancelText = null)
        => await ShowCoreAsync(title, message, DialogKind.MultiOption,
            primaryText, secondaryText, cancelText);

    public async Task<(bool Confirmed, string Input)> ShowInputAsync(string title, string prompt, string initialValue = "")
    {
        var host = await ResolveHostAsync();
        if (host is null) return (false, "");

        var tcs = new TaskCompletionSource<(bool, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var content = new InputContent { Prompt = prompt, Input = initialValue };
        content.Completed += (c, confirmed) => tcs.TrySetResult((confirmed, c.Input));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            host.DialogOverlayContent.Content = WrapOverlay(content, host, 400, 320);
            host.DialogOverlayHost.IsVisible = true;
            content.OnContentAttached();
        });

        var result = await tcs.Task;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            host.DialogOverlayHost.IsVisible = false;
            host.DialogOverlayContent.Content = null;
        });
        return result;
    }

    private async Task ShowAsync(string title, string message, DialogKind kind)
        => await ShowCoreAsync(title, message, kind);

    public async Task<SeatSetsExportSelection?> ShowSeatSetsSelectionAsync(
        bool isExport, SeatSetsExportSelection? available = null, CancellationToken ct = default)
    {
        var host = await ResolveHostAsync();
        if (host is null) return null;

        var tcs = new TaskCompletionSource<SeatSetsExportSelection?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new SeatSetsSelectionViewModel { IsExport = isExport };
        if (!isExport && available is not null)
        {
            viewModel.SetAvailableCategories(
                available.IncludeAppSettings, available.IncludeVenues, available.IncludeRosters,
                available.IncludeSnapshots, available.IncludeStrategyConfig);
        }

        var content = new SeatSetsSelectionContent { DataContext = viewModel };
        content.Completed += confirmed =>
            tcs.TrySetResult(confirmed ? viewModel.ToSelection() : null);

        // 与会话内其他 overlay 一致：令牌化浮层卡片承载内容
        var panel = new Border { Child = content };
        panel.Classes.Add("sf-modal");

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            host.DialogOverlayContent.Content = WrapOverlay(panel, host, 560, 640);
            host.DialogOverlayHost.IsVisible = true;
        });

        var result = await tcs.Task;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            host.DialogOverlayHost.IsVisible = false;
            host.DialogOverlayContent.Content = null;
        });
        return result;
    }

    private Task<MainView?> ResolveHostAsync()
    {
        // 浏览器端优先从单视图宿主直接取 MainView（无需 TopLevel 引用）
        var lifetime = global::Avalonia.Application.Current?.ApplicationLifetime;
        if (lifetime is ISingleViewApplicationLifetime { MainView: MainView view })
            return Task.FromResult<MainView?>(view);

        // 兜底：从 SetTopLevel 传入的 TopLevel 可视树中定位
        var resolved = _topLevel?.GetVisualDescendants().OfType<MainView>().FirstOrDefault();
        return Task.FromResult(resolved);
    }

    /// <summary>
    /// 按视口钳制 overlay 内容尺寸（手机档窄视口不溢出），内容超出高度时可垂直滚动。
    /// </summary>
    private static Control WrapOverlay(Control content, MainView host, double designWidth, double designHeight)
    {
        var availableWidth = DialogSizing.ResolveMaxWidth(designWidth, host.Bounds.Width);
        // 显式宽度（而非仅 MaxWidth）：短内容确认框也保持统一的设计宽度，与桌面对话框视觉一致
        var targetWidth = Math.Min(designWidth, availableWidth);
        content.Width = targetWidth;
        content.MaxWidth = targetWidth;
        return new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxWidth = targetWidth,
            MaxHeight = DialogSizing.ResolveMaxHeight(designHeight, host.Bounds.Height)
        };
    }
}
