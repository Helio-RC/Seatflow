#if BROWSER
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Avalonia.Threading;

namespace SeatFlow.Presentation.Avalonia.Services.Web;

/// <summary>
/// 浏览器端未保存更改守卫：把当前页面的 <see cref="IPageLifecycle.IsDirty"/> 镜像到
/// <c>sf.lifecycle</c> 模块，由 <c>beforeunload</c> 同步弹出浏览器关闭/刷新确认。
/// 轮询而非订阅：部分页面的 <c>IsDirty</c> 是计算属性（如 DirtyTracker），不保证发出通知。
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class WebUnsavedChangesGuard
{
    private readonly INavigationService _navigation;
    private readonly DispatcherTimer _timer;
    private bool? _lastDirty;

    [JSImport("setUnsavedChanges", "sf.lifecycle")]
    private static partial void SetUnsavedChanges(bool hasChanges);

    public WebUnsavedChangesGuard(INavigationService navigation)
    {
        _navigation = navigation;
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Background,
            (_, _) => Sync());
    }

    /// <summary>在 Avalonia 调度器就绪后调用。</summary>
    public void Start()
    {
        Sync();
        _timer.Start();
    }

    private void Sync()
    {
        var dirty = _navigation.CurrentViewModel is IPageLifecycle { IsDirty: true };
        if (_lastDirty == dirty) return;

        try
        {
            SetUnsavedChanges(dirty);
            _lastDirty = dirty;
        }
        catch
        {
            // 模块尚未加载完成时静默，下个 tick 重试。
        }
    }
}
#endif
