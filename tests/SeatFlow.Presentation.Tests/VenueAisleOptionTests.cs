using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 过道配置项：短标签（第 N 列/排后）、区间 tooltip，以及每桌人数变化时的迁移与勾选保留。
/// </summary>
public class VenueAisleOptionTests
{
    private static ClassroomLayoutDefinition CreateLayout(
        int rows = 5,
        int columns = 8,
        int seatsPerDesk = 2)
        => new()
        {
            Id = "v1",
            Name = "过道测试",
            LayoutType = LayoutType.Grid,
            Metadata = new GridLayoutMetadata
            {
                Rows = rows,
                Columns = columns,
                SeatsPerDesk = seatsPerDesk,
            },
        };

    private static async Task<VenueConfigurationViewModel> LoadAsync(ClassroomLayoutDefinition layout)
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.LoadVenueAsync("v1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ClassroomLayoutDefinition?>(layout));

        var vm = new VenueConfigurationViewModel(
            facade,
            Substitute.For<IFileService>(),
            Substitute.For<IDialogGate>(),
            NullDialogService.Instance);

        vm.SelectedVenueItem = new VenueItem("v1", layout.Name);
        await WaitUntilAsync(() => vm.AisleColumnOptions.Count > 0 && vm.AisleRowOptions.Count > 0);
        return vm;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var started = Environment.TickCount64;
        while (!condition() && Environment.TickCount64 - started < timeoutMs)
            await Task.Delay(15);

        if (!condition())
            throw new TimeoutException($"等待条件超时（{timeoutMs}ms）");
    }

    [AvaloniaFact]
    public async Task 列过道_短标签与区间提示()
    {
        var vm = await LoadAsync(CreateLayout(columns: 8, seatsPerDesk: 2));

        // 8 列双人桌 → 4 桌 → 3 个过道位（第 2/4/6 列后）
        vm.AisleColumnOptions.Should().HaveCount(3);
        vm.AisleColumnOptions[1].SeatColumn.Should().Be(4);
        vm.AisleColumnOptions[1].Label.Should().Be(string.Format(Resources.Venue_ColAisleFmt, 4));
        vm.AisleColumnOptions[1].ToolTip.Should().Be(string.Format(Resources.Venue_ColAisleTooltipFmt, 4, 5));
    }

    [AvaloniaFact]
    public async Task 行过道_短标签与区间提示()
    {
        var vm = await LoadAsync(CreateLayout(rows: 5));

        vm.AisleRowOptions.Should().HaveCount(4);
        vm.AisleRowOptions[0].Label.Should().Be(string.Format(Resources.Venue_RowAisleFmt, 1));
        vm.AisleRowOptions[0].ToolTip.Should().Be(string.Format(Resources.Venue_RowAisleTooltipFmt, 1, 2));
    }

    [AvaloniaFact]
    public async Task 每桌人数变化_过道选项迁移并保留勾选()
    {
        var vm = await LoadAsync(CreateLayout(columns: 8, seatsPerDesk: 2));
        vm.AisleColumnOptions[0].IsSelected = true;

        vm.GridSeatsPerDesk = 4;
        await WaitUntilAsync(() => vm.AisleColumnOptions.Count == 1);

        vm.AisleColumnOptions[0].SeatColumn.Should().Be(4);
        vm.AisleColumnOptions[0].IsSelected.Should().BeTrue();
        vm.GridAisleAfterColumns.Should().Be("4");
    }
}
