using System;
using Microsoft.Extensions.DependencyInjection;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 引导演示数据注入目标的 DI 注册（M5）：桌面/浏览器双壳 Program 共用，
/// 避免新增 seed 页面时两端漏注册导致引导期 <see cref="IServiceProvider"/> 缺项。
/// </summary>
public static class GuideSeedTargetRegistration
{
    /// <summary>把 5 个页面 ViewModel 注册为 <see cref="IGuideSeedTarget"/>（与各页单例同实例）。</summary>
    public static IServiceCollection AddGuideSeedTargets(this IServiceCollection services)
    {
        services.AddSingleton<IGuideSeedTarget>(sp => sp.GetRequiredService<MemberManagementViewModel>());
        services.AddSingleton<IGuideSeedTarget>(sp => sp.GetRequiredService<VenueConfigurationViewModel>());
        services.AddSingleton<IGuideSeedTarget>(sp => sp.GetRequiredService<StrategyConfigurationViewModel>());
        services.AddSingleton<IGuideSeedTarget>(sp => sp.GetRequiredService<SeatingArrangementViewModel>());
        services.AddSingleton<IGuideSeedTarget>(sp => sp.GetRequiredService<SnapshotHistoryViewModel>());
        return services;
    }
}
