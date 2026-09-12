using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
            host.DialogOverlayContent.Content = content;
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
            host.DialogOverlayContent.Content = content;
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
}
