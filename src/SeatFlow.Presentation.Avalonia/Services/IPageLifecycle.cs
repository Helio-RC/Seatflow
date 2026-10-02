using System.Threading;
using System.Threading.Tasks;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 页面生命周期契约（06 计划 §5 / 03 §6）：
/// 进入页面时显式加载（禁止构造器 fire-and-forget），离开时取消在途任务。
/// 页面 ViewModel 实现本接口后由导航服务驱动。
/// </summary>
public interface IPageLifecycle
{
    /// <summary>是否存在未保存的更改（导航离开时用于确认）。</summary>
    bool IsDirty { get; }

    /// <summary>
    /// 本次进入页面的初始化完成信号：OnEnterAsync 完成后置位、离开时换新未完成实例。
    /// 供引导系统等待「页面异步加载完成后再注入演示数据」，避免注入被后续加载覆盖。
    /// </summary>
    Task InitializationTask { get; }

    /// <summary>进入页面时调用；<paramref name="ct"/> 在离开页面时取消。</summary>
    Task OnEnterAsync(CancellationToken ct);

    /// <summary>离开页面时调用（取消在途加载、释放临时资源）。</summary>
    Task OnLeaveAsync();
}
