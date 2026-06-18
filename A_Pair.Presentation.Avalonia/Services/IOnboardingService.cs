using System.Threading.Tasks;
using CodeWF.AvaloniaControls.Controls;

namespace A_Pair.Presentation.Avalonia.Services;

/// <summary>引导服务——纯机械式桥接 JSON 配置与 Guide 控件，零业务逻辑推断。</summary>
public interface IOnboardingService
{
    /// <summary>引导是否当前活跃。</summary>
    bool IsActive { get; }

    /// <summary>启动（或重新启动）引导。</summary>
    void StartOnboarding();

    /// <summary>
    /// 在 Guide 控件显示步骤前调用。解析 Target 控件并处理跨阶段页面导航。
    /// </summary>
    /// <param name="stepIndex">全量平铺步骤列表中的索引。</param>
    /// <param name="step">可修改的步骤选项（设置 Target 等）。</param>
    void HandleStepOpening(int stepIndex, IGuideStepOption step);

    /// <summary>用户点击最后一步的"完成"按钮。</summary>
    void HandleGuideCompleted();

    /// <summary>用户点击 × 或按 Esc。返回 false 表示用户取消关闭（应重新显示 Guide）。</summary>
    Task<bool> HandleGuideClosedAsync();
}
