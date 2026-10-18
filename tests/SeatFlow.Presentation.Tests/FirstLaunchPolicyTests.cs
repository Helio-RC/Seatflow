using FluentAssertions;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Tests;

/// <summary>首启判定：真实首启、重跑标记、.seatsets 自动导入任一成立即进入 OOBE。</summary>
public class FirstLaunchPolicyTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void ShouldEnterOobe_任一条件成立_返回true(
        bool settingsFileMissing,
        bool isFirstLaunchFlag,
        bool seatSetsImported)
    {
        FirstLaunchPolicy.ShouldEnterOobe(settingsFileMissing, isFirstLaunchFlag, seatSetsImported)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldEnterOobe_全部不成立_返回false()
    {
        FirstLaunchPolicy.ShouldEnterOobe(false, false, false).Should().BeFalse();
    }
}
