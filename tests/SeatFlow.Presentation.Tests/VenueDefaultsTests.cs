using System.Windows.Input;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
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
}
