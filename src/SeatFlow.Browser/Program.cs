using System;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SeatFlow.Application.Interfaces;
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

        // 浏览器日志：转发到开发者工具 Console（AddSeatFlowApplication(store) 默认无 Provider）
        services.AddLogging(builder => builder
            .AddProvider(new BrowserConsoleLoggerProvider())
            .SetMinimumLevel(LogLevel.Information));

        // 平台服务：浏览器端实现（IndexedDB 存储 / overlay 对话框 / 占位文件与更新服务）
        services.AddSingleton<INavigationService , NavigationService>();
        services.AddSingleton<IFileService , WebFileService>();
        services.AddSingleton<IDialogService , WebDialogService>();
        services.AddSingleton<IUrlOpener , WebUrlOpener>();
        services.AddSingleton<IUpdateService , WebNoopUpdateService>();
        services.AddSingleton<IArrangementCounterService , ArrangementCounterService>();
        services.AddSingleton<ITelemetryService , NullTelemetryService>();

        // 注册 ViewModels（与桌面端 Program 保持一致）
        // 浏览器端不能构造 Window，外壳使用 MainView（UserControl）
        services.AddSingleton<MainView>();
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

        var serviceProvider = services.BuildServiceProvider();

        // 浏览器端禁止同步阻塞等待（App.Initialize 内的同步读取会抛
        // PlatformNotSupportedException：Cannot wait on monitors）。
        // 因此语言必须在 Avalonia 启动前异步预加载，确保 {x:Static} 资源字符串
        // 在 XAML 加载时按正确文化解析。
        try
        {
            var settings = await serviceProvider
                .GetRequiredService<IApplicationFacade>()
                .LoadAppSettingsAsync();
            App.ApplyLanguage(settings.Language);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SF] 预加载语言设置失败（使用系统默认文化）: {ex.Message}");
        }

        await StartBrowserAppAsync(serviceProvider);
    }

    [SupportedOSPlatform("browser")]
    private static Task StartBrowserAppAsync (IServiceProvider services)
        => BuildAvaloniaApp(services)
            .WithInterFont()
            .With(new FontManagerOptions
            {
                // WASM 无系统字体：CJK 字形通过 FontFallbacks 回退到嵌入的 Noto Sans SC
                FontFallbacks =
                [
                    new FontFallback
                    {
                        FontFamily = new FontFamily(
                            "avares://SeatFlow.Presentation.Avalonia/Assets/Fonts/NotoSansSC-Regular.otf#Noto Sans SC")
                    }
                ]
            })
            .LogToTrace()
            .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp (IServiceProvider services)
        => AppBuilder.Configure(() => new App(services , isFirstInstance: true));
}
