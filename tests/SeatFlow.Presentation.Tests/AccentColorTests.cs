using Avalonia.Media;
using FluentAssertions;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 主题色应用器测试（用户需求：主题色可调 默认 #83B6DE / 跟随系统）：
/// 前景色按相对亮度自动选择，保证浅色主题色上为深色文字、深色主题色上为白字。
/// </summary>
public class AccentColorTests
{
    [Theory]
    [InlineData("#83B6DE", false)] // 默认浅蓝 → 深色文字（对比度更高）
    [InlineData("#3B5BDB", true)]  // 深靛蓝 → 白字
    [InlineData("#FFFFFF", false)] // 纯白 → 深字
    [InlineData("#000000", true)]  // 纯黑 → 白字
    public void 主题色前景按对比度自动选择(string hex, bool expectWhite)
    {
        var foreground = AccentColorApplier.ChooseForeground(Color.Parse(hex));

        (foreground == Color.Parse("#FFFFFF")).Should().Be(expectWhite);
    }

    [Fact]
    public void 默认主题色亮度低于白字阈值_应选择深色前景()
    {
        AccentColorApplier.RelativeLuminance(AccentColorApplier.DefaultAccent)
            .Should().BeGreaterThan(0.42, "默认主题色 #83B6DE 是浅色，需要深色文字");
    }
}
