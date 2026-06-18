using System.Threading.Tasks;

namespace A_Pair.Presentation.Avalonia.Services;

/// <summary>引导服务——JSON 驱动，使用 Grid 层控件（非 Popup），与主页面同层级。</summary>
public interface IOnboardingService
{
    bool IsActive { get; }

    /// <summary>启动引导。</summary>
    void StartOnboarding();

    /// <summary>页面引导（首次访问触发）。</summary>
    bool TryShowPageGuide(PageKey page);

    /// <summary>标记页面引导已展示。</summary>
    Task MarkPageGuideShownAsync(PageKey page);

    /// <summary>上一步。</summary>
    void GoToPreviousStep();

    /// <summary>下一步。</summary>
    void GoToNextStep();

    /// <summary>完成引导。</summary>
    void HandleGuideCompleted();

    /// <summary>关闭引导。返回 false 表示取消。</summary>
    Task<bool> HandleGuideClosedAsync();

    /// <summary>重新显示卡片（关闭取消后）。</summary>
    void ShowCard();
}
