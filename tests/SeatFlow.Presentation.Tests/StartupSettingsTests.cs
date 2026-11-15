using FluentAssertions;
using NSubstitute;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 环境警告“不再提醒”只更新单个字段：重新读取最新设置后再保存，
/// 不得回滚 IsFirstLaunch / 遥测同意等首启期间写入的字段。
/// </summary>
public class StartupSettingsTests
{
    [Fact]
    public async Task 不再提醒_保留首启标记与遥测同意()
    {
        var settings = new AppSettings
        {
            IsFirstLaunch = true,
            SuppressEnvironmentWarning = false,
            Telemetry = new TelemetryConfig { Enabled = true, ConsentShown = true },
        };
        var repo = Substitute.For<IAppSettingsRepository>();
        repo.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(settings));

        await SeatFlow.Presentation.Avalonia.App.SuppressEnvironmentWarningAsync(repo);

        settings.SuppressEnvironmentWarning.Should().BeTrue();
        settings.IsFirstLaunch.Should().BeTrue("首启标记不得被环境警告写盘回滚");
        settings.Telemetry.ConsentShown.Should().BeTrue("遥测同意不得被环境警告写盘回滚");
        await repo.Received(1).SaveAsync(settings, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 已抑制环境警告_不重复写盘()
    {
        var settings = new AppSettings { SuppressEnvironmentWarning = true };
        var repo = Substitute.For<IAppSettingsRepository>();
        repo.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(settings));

        await SeatFlow.Presentation.Avalonia.App.SuppressEnvironmentWarningAsync(repo);

        await repo.DidNotReceive().SaveAsync(Arg.Any<AppSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 更新失败不再提示_只写该字段()
    {
        var settings = new AppSettings
        {
            IsFirstLaunch = true,
            SuppressUpdateFailedNotice = false,
            AutoUpdate = AutoUpdateMode.CheckOnly,
        };
        var repo = Substitute.For<IAppSettingsRepository>();
        repo.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(settings));

        await SeatFlow.Presentation.Avalonia.App.SuppressUpdateFailedNoticeAsync(repo);

        settings.SuppressUpdateFailedNotice.Should().BeTrue();
        settings.SuppressEnvironmentWarning.Should().BeFalse("不得顺带改掉环境警告的抑制标记");
        settings.AutoUpdate.Should().Be(AutoUpdateMode.CheckOnly, "只抑制提示，不改变检查行为");
        settings.IsFirstLaunch.Should().BeTrue();
        await repo.Received(1).SaveAsync(settings, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 已抑制更新失败提示_不重复写盘()
    {
        var settings = new AppSettings { SuppressUpdateFailedNotice = true };
        var repo = Substitute.For<IAppSettingsRepository>();
        repo.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(settings));

        await SeatFlow.Presentation.Avalonia.App.SuppressUpdateFailedNoticeAsync(repo);

        await repo.DidNotReceive().SaveAsync(Arg.Any<AppSettings>(), Arg.Any<CancellationToken>());
    }
}
