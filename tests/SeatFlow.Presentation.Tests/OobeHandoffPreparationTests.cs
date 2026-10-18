using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>OOBE 交接准备：按 id 选中数据；缺 id/找不到时必须清空旧工作区选择。</summary>
public class OobeHandoffPreparationTests
{
    private static SeatingArrangementViewModel CreateVm(
        IApplicationFacade facade,
        out ClassroomLayoutDefinition layout)
    {
        layout = new ClassroomLayoutDefinition
        {
            Id = "venue-1",
            Name = "演示教室",
            Metadata = new GridLayoutMetadata { Rows = 2, Columns = 2, SeatsPerDesk = 1 },
        };
        layout.Seats.Add(new GridSeat { Row = 1, Column = 1 });
        var createdLayout = layout;

        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<VenueSummary>>([new VenueSummary("venue-1", "演示教室")]));
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<StudentDatasetInfo>>(
                [new StudentDatasetInfo { Id = "ds-1", Name = "高一(1)班", StudentCount = 1 }]));
        facade.LoadVenueAsync("venue-1", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<ClassroomLayoutDefinition?>(createdLayout));

        return new SeatingArrangementViewModel(
            facade,
            Substitute.For<IFileService>(),
            Substitute.For<IArrangementCounterService>(),
            new ShellLayoutService(),
            new WelcomeCardViewModel(NullDialogService.Instance),
            Substitute.For<IServiceProvider>(),
            NullDialogService.Instance);
    }

    private static void SeedStaleSelection(SeatingArrangementViewModel vm)
    {
        vm.VenueItems.Clear();
        vm.VenueItems.Add(new VenueItem("stale-venue", "旧会场"));
        vm.DatasetItems.Clear();
        vm.DatasetItems.Add(new StudentDatasetInfo { Id = "stale-ds", Name = "旧名单" });
        vm.SelectedVenue = vm.VenueItems[0];
        vm.SelectedDataset = vm.DatasetItems[0];
        vm.HasGenerated = true;
    }

    [AvaloniaFact]
    public async Task 缺少id_清空旧选择_不沿用上次工作区()
    {
        var facade = Substitute.For<IApplicationFacade>();
        var vm = CreateVm(facade, out _);
        SeedStaleSelection(vm);

        var result = await vm.PrepareOobeHandoffAsync(null, null);

        result.IsReady.Should().BeFalse();
        vm.SelectedVenue.Should().BeNull("缺会场 id 时不得沿用旧选择");
        vm.SelectedDataset.Should().BeNull("缺名单 id 时不得沿用旧选择");
        vm.HasGenerated.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task id不存在_清空旧选择()
    {
        var facade = Substitute.For<IApplicationFacade>();
        var vm = CreateVm(facade, out _);
        SeedStaleSelection(vm);

        var result = await vm.PrepareOobeHandoffAsync("missing-venue", "missing-ds");

        result.IsReady.Should().BeFalse();
        vm.SelectedVenue.Should().BeNull();
        vm.SelectedDataset.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task 有效id_选中会场与名单()
    {
        var facade = Substitute.For<IApplicationFacade>();
        var vm = CreateVm(facade, out var layout);
        SeedStaleSelection(vm);

        var result = await vm.PrepareOobeHandoffAsync("venue-1", "ds-1");

        result.IsReady.Should().BeTrue();
        vm.SelectedVenue!.Id.Should().Be("venue-1");
        vm.SelectedDataset!.Id.Should().Be("ds-1");
    }

    [AvaloniaFact]
    public async Task 交接完成后_刷新仍恢复工作区()
    {
        var facade = Substitute.For<IApplicationFacade>();
        var vm = CreateVm(facade, out _);

        await vm.PrepareOobeHandoffAsync("venue-1", "ds-1");
        await vm.RefreshDataAsync();

        await facade.Received(1).GetCurrentWorkspaceAsync(Arg.Any<CancellationToken>());
    }
}
