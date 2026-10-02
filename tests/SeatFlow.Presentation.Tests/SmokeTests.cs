using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using AvaloniaApplication = Avalonia.Application;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// Headless 测试基建冒烟测试（M6）：
/// 验证 Avalonia.Headless.XUnit 12.1.3（按 xunit.v3 3.2.2 编译）在
/// 本项目锁定版本下可发现并运行，以及测试 Application 的资源装配（令牌可解析、可渲染真实 Skia 帧）。
/// </summary>
public class SmokeTests
{
    [AvaloniaFact]
    public void Headless_应用可创建窗口并渲染()
    {
        var window = new Window
        {
            Width = 400,
            Height = 300,
            Content = new TextBlock { Text = "SeatFlow Headless" },
        };
        window.Show();

        var frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void 令牌资源在测试应用可解析()
    {
        var app = AvaloniaApplication.Current!;
        app.TryFindResource("SfAccentBrush", out var brush).Should().BeTrue();
        brush.Should().NotBeNull();
    }
}
