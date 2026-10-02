using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Telemetry;
using SeatFlow.Presentation.Avalonia.Behaviors;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using NSubstitute;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 设置页 ViewModel 的紧凑断点测试（M6）：
/// <see cref="SettingsViewModel.CardColumns"/> 随 <see cref="IShellLayoutService.IsCompact"/> 切换，
/// 并经属性变更通知驱动视图重排。
/// </summary>
public class SettingsViewModelTests
{
    private static SettingsViewModel CreateViewModel(ShellLayoutService layout) => new(
        Substitute.For<IApplicationFacade>(),
        NullDialogService.Instance,
        Substitute.For<IOnboardingService>(),
        Substitute.For<IFileService>(),
        Substitute.For<ITelemetryService>(),
        Substitute.For<IUpdateService>(),
        Substitute.For<IUrlOpener>(),
        Substitute.For<IServiceProvider>(),
        new KeyboardShortcutHandler(),
        new DialogGate(),
        layout);

    [Fact]
    public void CardColumns_桌面两列_紧凑单列_切回两列()
    {
        var layout = new ShellLayoutService();
        var vm = CreateViewModel(layout);

        vm.CardColumns.Should().Be(2);

        layout.IsCompact = true;
        vm.CardColumns.Should().Be(1);

        layout.IsCompact = false;
        vm.CardColumns.Should().Be(2);
    }

    [Fact]
    public void 断点变化触发_CardColumns_变更通知()
    {
        var layout = new ShellLayoutService();
        var vm = CreateViewModel(layout);

        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        layout.IsCompact = true;

        changes.Should().Contain(nameof(SettingsViewModel.CardColumns));
    }

    [Fact]
    public void 主题色选项_默认与跟随系统两项_默认索引为0()
    {
        var vm = CreateViewModel(new ShellLayoutService());

        vm.AccentColorOptions.Should().HaveCount(2);
        vm.AccentColorIndex.Should().Be(0, "默认使用内置主题色 #83B6DE");
    }
}
