using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 排座工作台命令状态测试（M6）：
/// 生成 / 创建空白的可执行性由「已选会场 + 已选名单 + 未在生成中」驱动，
/// 并随属性变化发出通知。
/// </summary>
public class SeatingCommandStateTests
{
    private static SeatingArrangementViewModel CreateVm() => new(
        Substitute.For<IApplicationFacade>(),
        Substitute.For<IFileService>(),
        Substitute.For<IArrangementCounterService>(),
        new ShellLayoutService(),
        new WelcomeCardViewModel(NullDialogService.Instance),
        Substitute.For<IServiceProvider>(),
        NullDialogService.Instance);

    [AvaloniaFact]
    public void 未选数据时_生成与创建空白均不可执行()
    {
        var vm = CreateVm();

        vm.CanGenerate.Should().BeFalse();
        vm.CanCreateEmpty.Should().BeFalse();
        vm.HasSelectedVenue.Should().BeFalse();
        vm.HasSelectedDataset.Should().BeFalse();
    }

    [AvaloniaFact]
    public void 仅选会场_仍不可生成_补齐名单后启用()
    {
        var vm = CreateVm();

        vm.SelectedVenue = new VenueItem("v1", "测试会场");
        vm.CanGenerate.Should().BeFalse();

        vm.SelectedDataset = new StudentDatasetInfo { Id = "d1", Name = "测试名单" };
        vm.HasSelectedDataset.Should().BeTrue();
        vm.CanGenerate.Should().BeTrue();
        vm.CanCreateEmpty.Should().BeTrue();
    }

    [AvaloniaFact]
    public void 生成中_生成与创建空白被禁用()
    {
        var vm = CreateVm();
        vm.SelectedVenue = new VenueItem("v1", "测试会场");
        vm.SelectedDataset = new StudentDatasetInfo { Id = "d1", Name = "测试名单" };

        vm.IsGenerating = true;

        vm.CanGenerate.Should().BeFalse();
        vm.CanCreateEmpty.Should().BeFalse();
    }

    [AvaloniaFact]
    public void 数据选择变化触发_CanGenerate_变更通知()
    {
        var vm = CreateVm();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.SelectedVenue = new VenueItem("v1", "测试会场");
        vm.SelectedDataset = new StudentDatasetInfo { Id = "d1", Name = "测试名单" };

        changes.Should().Contain(nameof(SeatingArrangementViewModel.CanGenerate));
        changes.Should().Contain(nameof(SeatingArrangementViewModel.CanCreateEmpty));
    }

    [AvaloniaFact]
    public void 初始撤销重做均不可用()
    {
        var vm = CreateVm();

        vm.CanUndo.Should().BeFalse();
        vm.CanRedo.Should().BeFalse();
    }
}
