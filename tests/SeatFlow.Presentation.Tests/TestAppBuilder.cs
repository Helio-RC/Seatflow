using Avalonia;
using Avalonia.Headless;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// Headless 测试应用构建器（M6）：
/// 复用生产 <see cref="SeatFlow.Presentation.Avalonia.App"/> 实例（完整加载 App.axaml 的
/// 主题/令牌/样式资源，避免测试与生产资源漂移），仅注入最小 DI（语言设置读取用 facade 替身）。
/// <para>
/// <see cref="AvaloniaHeadlessPlatformOptions.UseHeadlessDrawing"/> 置 false 启用 Skia 真实绘制，
/// 供视觉基线捕获测试使用。
/// </para>
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.LoadAppSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new AppSettings { Language = "zh-CN" });

        var services = new ServiceCollection();
        services.AddSingleton(facade);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        return AppBuilder
            .Configure(() => new SeatFlow.Presentation.Avalonia.App(provider))
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .LogToTrace();
    }
}
