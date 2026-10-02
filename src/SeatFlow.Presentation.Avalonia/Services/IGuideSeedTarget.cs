namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 引导演示数据注入契约（M5）：
/// 由页面 ViewModel 自行实现「注入演示数据 / 清理并恢复原状态」，
/// <c>OnboardingService</c> 只按接口调用，不再直接操作页面内部状态。
/// 约定：注入为纯内存操作、不落盘；实现需幂等（未注入时清理无副作用）。
/// </summary>
public interface IGuideSeedTarget
{
    /// <summary>注入引导演示数据（保存注入前状态供清理恢复）。</summary>
    void SeedGuideData();

    /// <summary>清理引导演示数据并恢复注入前状态（未注入过则无操作）。</summary>
    void ClearGuideData();
}
