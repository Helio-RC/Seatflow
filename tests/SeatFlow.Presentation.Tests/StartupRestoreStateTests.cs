using FluentAssertions;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Tests;

/// <summary>启动恢复状态默认值与赋值。</summary>
public class StartupRestoreStateTests
{
    [Fact]
    public void 默认值_未导入()
    {
        var state = new StartupRestoreState();

        state.SeatSetsImported.Should().BeFalse();
        state.RestoredFiles.Should().Be(0);
        state.FailedFiles.Should().Be(0);
        state.Errors.Should().BeEmpty();
    }

    [Fact]
    public void 赋值_保留导入结果()
    {
        var state = new StartupRestoreState
        {
            SeatSetsImported = true,
            RestoredFiles = 3,
            FailedFiles = 1,
            Errors = ["bad.json"],
        };

        state.SeatSetsImported.Should().BeTrue();
        state.RestoredFiles.Should().Be(3);
        state.FailedFiles.Should().Be(1);
        state.Errors.Should().ContainSingle().Which.Should().Be("bad.json");
    }
}
