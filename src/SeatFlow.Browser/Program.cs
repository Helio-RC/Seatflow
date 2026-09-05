using System;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Microsoft.Extensions.DependencyInjection;
using SeatFlow.Application.Services;
using SeatFlow.Core.Telemetry;
using SeatFlow.Infrastructure.Storage;
using SeatFlow.Presentation.Avalonia;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.Services.Web;
using SeatFlow.Presentation.Avalonia.Telemetry;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Presentation.Avalonia.Views;

namespace SeatFlow.Browser;

internal sealed class Program
{
    [SupportedOSPlatform("browser")]
    private static async Task Main (string[] args)
    {
        // 加载 IndexedDB 桥接模块（ILocalDataStore 的 WASM 实现依赖）
        await JSHost.ImportAsync("sf.idb" , "/js/interop.js");
        // 加载文件互操作模块（打开/保存）
        await JSHost.ImportAsync("sf.files" , "/js/files.js");

        var services = new ServiceCollection();

        // 存储抽象：WASM = IndexedDB（无文件系统；桌面端走 AddSeatFlowApplication(string)）
        services.AddSeatFlowApplication(new IndexedDbDataStore());

        // 平台服务：浏览器端实现（IndexedDB 存储 / overlay 对话框 / 占位文件与更新服务）
        services.AddSingleton<INavigationService , NavigationService>();
        services.AddSingleton<IFileService , WebFileService>();
        services.AddSingleton<IDialogService , WebDialogService>();
        services.AddSingleton<IUrlOpener , WebUrlOpener>();
        services.AddSingleton<IUpdateService , WebNoopUpdateService>();
        services.AddSingleton<IArrangementCounterService , ArrangementCounterService>();
        services.AddSingleton<ITelemetryService , NullTelemetryService>();

        // 注册 ViewModels（与桌面端 Program 保持一致）
        services.AddSingleton<MainWindow>();
        services.AddSingleton<IOnboardingService , OnboardingService>();
        services.AddSingleton<IOnboardingStarter>(sp => (IOnboardingStarter)sp.GetRequiredService<IOnboardingService>());
        services.AddSingleton<MainShellViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<MemberManagementViewModel>();
        services.AddSingleton<VenueConfigurationViewModel>();
        services.AddSingleton<FreeformManagementViewModel>();
        services.AddSingleton<StrategyConfigurationViewModel>();
        services.AddSingleton<SeatingArrangementViewModel>();
        services.AddTransient<SnapshotHistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddTransient<ConfigBlockEditorViewModel>();
        services.AddTransient<UpdateDialogViewModel>();

        await StartBrowserAppAsync(services.BuildServiceProvider());
    }

    [SupportedOSPlatform("browser")]
    private static Task StartBrowserAppAsync (IServiceProvider services)
        => BuildAvaloniaApp(services)
            .WithInterFont()
            .LogToTrace()
            .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp (IServiceProvider services)
        => AppBuilder.Configure(() => new App(services , isFirstInstance: true));
}
