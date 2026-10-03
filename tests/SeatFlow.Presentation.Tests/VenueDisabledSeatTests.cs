using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Presentation.Avalonia.Views;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 会场配置「每列行数」输入项与「禁用座位」拾取（草稿/保存/反选/清除）的 ViewModel 行为测试。
/// </summary>
public class VenueDisabledSeatTests
{
    private static ClassroomLayoutDefinition CreateGridLayout(
        int rows = 5,
        int columns = 3,
        List<int>? columnRowCounts = null,
        string id = "v1",
        string name = "测试会场")
        => new()
        {
            Id = id,
            Name = name,
            LayoutType = LayoutType.Grid,
            Metadata = new GridLayoutMetadata
            {
                Rows = rows,
                Columns = columns,
                ColumnRowCounts = columnRowCounts ?? [],
            },
        };

    private static async Task<VenueConfigurationViewModel> CreateLoadedVmAsync(ClassroomLayoutDefinition layout)
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.LoadVenueAsync("v1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ClassroomLayoutDefinition?>(layout));
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<VenueSummary>>([new VenueSummary("v1", layout.Name)]));

        var vm = new VenueConfigurationViewModel(
            facade,
            Substitute.For<IFileService>(),
            Substitute.For<IDialogGate>(),
            NullDialogService.Instance);

        vm.SelectedVenueItem = new VenueItem("v1", layout.Name);
        await WaitUntilAsync(() => vm.ColumnRowCountOptions.Count > 0 || vm.SelectedLayoutType == LayoutType.Polar);
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

    private static SeatVisual GridSeat(int row, int column)
        => new("s", 0, 0, 10, 10, Row: row, Column: column);

    [AvaloniaFact]
    public async Task 每列行数_中间空位按行数补齐_末尾省略()
    {
        var vm = await CreateLoadedVmAsync(CreateGridLayout(rows: 5, columns: 3));

        vm.ColumnRowCountOptions.Should().HaveCount(3);
        vm.ColumnRowCountOptions.Should().OnlyContain(o => o.Rows == null);
        vm.GridColumnRowCountsSpec.Should().Be("");

        vm.ColumnRowCountOptions[2].Rows = 6;

        vm.GridColumnRowCountsSpec.Should().Be("5,5,6", "中间空位按当前行数补齐");

        vm.ColumnRowCountOptions[0].Rows = 2;
        vm.ColumnRowCountOptions[2].Rows = null;

        vm.GridColumnRowCountsSpec.Should().Be("2", "末尾空位省略，之前空位按行数补齐");
    }

    [AvaloniaFact]
    public async Task 每列行数_列数变化_保留同列值并截断()
    {
        var vm = await CreateLoadedVmAsync(CreateGridLayout(rows: 5, columns: 4));
        vm.ColumnRowCountOptions[0].Rows = 2;
        vm.ColumnRowCountOptions[3].Rows = 9;

        vm.GridColumns = 2;
        await WaitUntilAsync(() => vm.ColumnRowCountOptions.Count == 2);

        vm.ColumnRowCountOptions.Should().HaveCount(2);
        vm.ColumnRowCountOptions[0].Rows.Should().Be(2);
        vm.GridColumnRowCountsSpec.Should().Be("2");

        vm.GridColumns = 5;
        await WaitUntilAsync(() => vm.ColumnRowCountOptions.Count == 5);

        vm.ColumnRowCountOptions[0].Rows.Should().Be(2);
        vm.ColumnRowCountOptions[4].Rows.Should().BeNull("新增列留空");
    }

    [AvaloniaFact]
    public async Task 禁用座位_草稿保存_反选_清除()
    {
        var vm = await CreateLoadedVmAsync(CreateGridLayout());

        vm.OnPreviewSeatClicked(GridSeat(3, 3));
        vm.GridEmptyPositionsSpec.Should().Be("", "非拾取模式下点击不产生效果");

        vm.ToggleDisabledSeatsPickingCommand.Execute(null);
        vm.IsPickingDisabledSeats.Should().BeTrue();

        vm.OnPreviewSeatClicked(GridSeat(1, 2));
        vm.OnPreviewSeatClicked(GridSeat(2, 3));

        vm.GridEmptyPositionsSpec.Should().Be("", "草稿保存前不写入 spec");

        vm.SaveDisabledSeatsPickingCommand.Execute(null);

        vm.IsPickingDisabledSeats.Should().BeFalse();
        vm.GridEmptyPositionsSpec.Should().Be("1,2;2,3");

        vm.ToggleDisabledSeatsPickingCommand.Execute(null);
        vm.OnPreviewSeatClicked(GridSeat(1, 2));
        vm.SaveDisabledSeatsPickingCommand.Execute(null);

        vm.GridEmptyPositionsSpec.Should().Be("2,3", "再次点击已禁用座位 = 反选恢复");

        vm.ClearDisabledSeatsCommand.Execute(null);

        vm.GridEmptyPositionsSpec.Should().Be("");
        vm.CanClearDisabledSeats.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task 禁用座位_清除全部_同时清空草稿并保持拾取模式()
    {
        var vm = await CreateLoadedVmAsync(CreateGridLayout());
        vm.ToggleDisabledSeatsPickingCommand.Execute(null);
        vm.OnPreviewSeatClicked(GridSeat(1, 1));
        vm.CanClearDisabledSeats.Should().BeTrue();

        vm.ClearDisabledSeatsCommand.Execute(null);

        vm.IsPickingDisabledSeats.Should().BeTrue("清除后保持选择模式");
        vm.CanClearDisabledSeats.Should().BeFalse();

        vm.SaveDisabledSeatsPickingCommand.Execute(null);
        vm.GridEmptyPositionsSpec.Should().Be("");
    }

    [AvaloniaFact]
    public async Task 禁用座位_切换布局自动应用草稿()
    {
        var vm = await CreateLoadedVmAsync(CreateGridLayout());
        vm.ToggleDisabledSeatsPickingCommand.Execute(null);
        vm.OnPreviewSeatClicked(GridSeat(1, 1));

        vm.SelectedLayoutType = LayoutType.Polar;

        vm.IsPickingDisabledSeats.Should().BeFalse();
        vm.GridEmptyPositionsSpec.Should().Be("1,1");
    }

    [AvaloniaFact]
    public async Task 视图_加载后渲染_并进入拾取模式()
    {
        var vm = await CreateLoadedVmAsync(CreateGridLayout());
        var view = new VenueConfigurationView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        window.UpdateLayout();

        vm.ToggleDisabledSeatsPickingCommand.Execute(null);
        window.UpdateLayout();

        vm.IsPickingDisabledSeats.Should().BeTrue();
        window.CaptureRenderedFrame().Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task 每列行数_切换会场_不沿用上一个会场取值()
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.LoadVenueAsync("v1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ClassroomLayoutDefinition?>(
                CreateGridLayout(columnRowCounts: [2, 3, 4])));
        facade.LoadVenueAsync("v2", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ClassroomLayoutDefinition?>(
                CreateGridLayout(id: "v2", name: "会场B")));

        var vm = new VenueConfigurationViewModel(
            facade,
            Substitute.For<IFileService>(),
            Substitute.For<IDialogGate>(),
            NullDialogService.Instance);

        vm.SelectedVenueItem = new VenueItem("v1", "会场A");
        await WaitUntilAsync(() => vm.GridColumnRowCountsSpec == "2,3,4");

        vm.SelectedVenueItem = new VenueItem("v2", "会场B");
        await WaitUntilAsync(() => vm.LayoutName == "会场B");

        vm.GridColumnRowCountsSpec.Should().Be("", "会场B 未设置每列行数");
        vm.ColumnRowCountOptions.Should().OnlyContain(o => o.Rows == null);
    }

    [AvaloniaFact]
    public async Task 每列行数_行数变化_内部空位随新行数补齐()
    {
        var vm = await CreateLoadedVmAsync(CreateGridLayout(rows: 5, columns: 3));
        vm.ColumnRowCountOptions[2].Rows = 6;
        vm.GridColumnRowCountsSpec.Should().Be("5,5,6");

        vm.GridRows = 7;
        await WaitUntilAsync(() => vm.GridColumnRowCountsSpec == "7,7,6");
    }

    [AvaloniaFact]
    public async Task 禁用座位_草稿阶段不标脏()
    {
        var vm = await CreateLoadedVmAsync(CreateGridLayout());
        vm.IsDirty.Should().BeFalse();

        vm.ToggleDisabledSeatsPickingCommand.Execute(null);
        vm.OnPreviewSeatClicked(GridSeat(1, 1));
        vm.IsDirty.Should().BeFalse("草稿保存前不标脏");

        vm.SaveDisabledSeatsPickingCommand.Execute(null);
        vm.IsDirty.Should().BeTrue("保存草稿后进入脏状态");
    }

    [AvaloniaFact]
    public async Task 禁用座位_拾取期间缩小列数_提交时过滤越界草稿()
    {
        var vm = await CreateLoadedVmAsync(CreateGridLayout(columns: 3));
        vm.GridColumns = 4;

        vm.ToggleDisabledSeatsPickingCommand.Execute(null);
        vm.OnPreviewSeatClicked(GridSeat(2, 4));

        vm.GridColumns = 2;
        vm.SaveDisabledSeatsPickingCommand.Execute(null);

        vm.GridEmptyPositionsSpec.Should().Be("", "越界草稿在提交时被过滤");
    }

    [AvaloniaFact]
    public async Task 禁用座位_Polar草稿按环与角度写入()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Id = "v1",
            Name = "环形会场",
            LayoutType = LayoutType.Polar,
            Metadata = new PolarLayoutMetadata { Rings = 2, SeatsPerRing = 8 },
        };
        var vm = await CreateLoadedVmAsync(layout);
        vm.SelectedLayoutType = LayoutType.Polar;

        vm.ToggleDisabledSeatsPickingCommand.Execute(null);
        vm.OnPreviewSeatClicked(new SeatVisual("p", 0, 0, 10, 10, Ring: 1, AngleDegrees: 90));
        vm.SaveDisabledSeatsPickingCommand.Execute(null);

        var parts = vm.PolarEmptyPositionsSpec.Split(';', ',');
        parts.Should().HaveCount(2);
        parts[0].Should().Be("1");
        double.Parse(parts[1], CultureInfo.CurrentCulture).Should().Be(90);
    }
}
