using System.Windows.Input;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>会场页「新建会场」与 OOBE 会场步骤共用同一组默认参数与命名规则。</summary>
public class VenueDefaultsTests
{
    [Fact]
    public void 新建会场_使用统一默认参数与八位ID命名()
    {
        var vm = new VenueConfigurationViewModel(
            Substitute.For<IApplicationFacade>(),
            Substitute.For<IFileService>(),
            Substitute.For<IDialogGate>(),
            new ShellLayoutService(),
            NullDialogService.Instance);

        ((ICommand)vm.NewVenueCommand).Execute(null);

        vm.GridRows.Should().Be(VenueLayoutDefaults.GridRows);
        vm.GridColumns.Should().Be(VenueLayoutDefaults.GridColumns);
        vm.GridHorizontalSpacing.Should().Be(VenueLayoutDefaults.GridHorizontalSpacing);
        vm.GridVerticalSpacing.Should().Be(VenueLayoutDefaults.GridVerticalSpacing);
        vm.GridOriginX.Should().Be(VenueLayoutDefaults.GridOriginX);
        vm.GridOriginY.Should().Be(VenueLayoutDefaults.GridOriginY);
        vm.GridSeatsPerDesk.Should().Be(VenueLayoutDefaults.GridSeatsPerDesk);
        vm.GridIntraDeskSpacing.Should().Be(VenueLayoutDefaults.GridIntraDeskSpacing);
        vm.GridInterDeskSpacing.Should().Be(VenueLayoutDefaults.GridInterDeskSpacing);
        vm.GridAisleWidth.Should().Be(VenueLayoutDefaults.GridAisleWidth);
        vm.GridFrontRowCount.Should().Be(VenueLayoutDefaults.GridFrontRowCount);
        vm.GridHasPodium.Should().Be(VenueLayoutDefaults.GridHasPodium);
        vm.GridPodiumWidth.Should().Be(VenueLayoutDefaults.GridPodiumWidth);
        vm.GridPodiumHeight.Should().Be(VenueLayoutDefaults.GridPodiumHeight);

        vm.SelectedVenueItem.Should().NotBeNull();
        vm.SelectedVenueItem!.Id.Should().HaveLength(8);
        vm.LayoutName.Should().Be($"新会场_{vm.SelectedVenueItem.Id}");
    }

    [AvaloniaFact]
    public async Task 载入零值间距的旧会场_回退到统一默认参数()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Id = "v1",
            Name = "旧会场",
            LayoutType = LayoutType.Grid,
            Metadata = new GridLayoutMetadata
            {
                Rows = 2,
                Columns = 3,
                HorizontalSpacing = 0,
                VerticalSpacing = 0,
                OriginX = 0,
                OriginY = 0,
                SeatsPerDesk = 0,
                IntraDeskSpacing = 0,
                InterDeskSpacing = 0,
                AisleWidth = 0,
                FrontRowCount = 0,
                PodiumWidth = 0,
                PodiumHeight = 0,
            },
        };
        var facade = Substitute.For<IApplicationFacade>();
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<VenueSummary>>([new VenueSummary("v1", "旧会场")]));
        facade.LoadVenueAsync("v1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ClassroomLayoutDefinition?>(layout));

        var vm = new VenueConfigurationViewModel(
            facade,
            Substitute.For<IFileService>(),
            Substitute.For<IDialogGate>(),
            new ShellLayoutService(),
            NullDialogService.Instance);

        await vm.OnEnterAsync(CancellationToken.None);
        vm.SelectedVenueItem = vm.VenueItems[0];
        await WaitUntilAsync(() => vm.GridColumns == 3);

        vm.GridHorizontalSpacing.Should().Be(VenueLayoutDefaults.GridHorizontalSpacing);
        vm.GridVerticalSpacing.Should().Be(VenueLayoutDefaults.GridVerticalSpacing);
        vm.GridOriginX.Should().Be(VenueLayoutDefaults.GridOriginX);
        vm.GridOriginY.Should().Be(VenueLayoutDefaults.GridOriginY);
        vm.GridSeatsPerDesk.Should().Be(VenueLayoutDefaults.GridSeatsPerDesk);
        vm.GridIntraDeskSpacing.Should().Be(VenueLayoutDefaults.GridIntraDeskSpacing);
        vm.GridInterDeskSpacing.Should().Be(VenueLayoutDefaults.GridInterDeskSpacing);
        vm.GridAisleWidth.Should().Be(VenueLayoutDefaults.GridAisleWidth);
        vm.GridFrontRowCount.Should().Be(VenueLayoutDefaults.GridFrontRowCount);
        vm.GridPodiumWidth.Should().Be(VenueLayoutDefaults.GridPodiumWidth);
        vm.GridPodiumHeight.Should().Be(VenueLayoutDefaults.GridPodiumHeight);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var started = Environment.TickCount64;
        while (!condition() && Environment.TickCount64 - started < timeoutMs)
            await Task.Delay(15);

        if (!condition())
            throw new TimeoutException($"等待条件超时（{timeoutMs}ms）");
    }
}
