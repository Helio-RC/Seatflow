using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 策略配置块的会场选择器：显示会场名称（而非 ID），且页面缓存命中时能刷新重命名。
/// </summary>
public class StrategyVenuePickerTests
{
    private const string VenueId = "137c15a8";

    private static StrategyDisplayInfo CreateStrategy() => new()
    {
        Id = "test-strategy",
        DisplayName = "测试策略",
        Priority = 10,
        IsEnabled = true,
        CodeBlocks =
        [
            new StrategyCodeBlock
            {
                Title = new Dictionary<string, string> { ["zh-CN"] = "固定座位分配" },
                DataType = StrategyDataType.Venue,
                ShowVenuePicker = true,
                ShowSeatPosition = false,
            },
        ],
    };

    private static IApplicationFacade CreateFacade(string venueName)
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.GetStrategiesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<StrategyDisplayInfo> { CreateStrategy() }));
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StudentDatasetInfo>>([]));
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<VenueSummary>>([new VenueSummary(VenueId, venueName)]));
        facade.LoadStrategyDatasetConfigsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<StrategyDatasetConfig>()));
        return facade;
    }

    private static StrategyConfigurationViewModel CreateVm(IApplicationFacade facade)
        => new(facade, new ShellLayoutService(), NullDialogService.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var started = Environment.TickCount64;
        while (!condition() && Environment.TickCount64 - started < timeoutMs)
            await Task.Delay(15);

        if (!condition())
            throw new TimeoutException($"等待条件超时（{timeoutMs}ms）");
    }

    [AvaloniaFact]
    public async Task 会场选择器_显示名称而非ID()
    {
        var facade = CreateFacade("会议室A");
        var vm = CreateVm(facade);

        await vm.OnEnterAsync(CancellationToken.None);
        vm.SelectedStrategy = vm.Strategies[0];
        await WaitUntilAsync(() => vm.ConfigBlockEditors.Count > 0);

        var venues = vm.ConfigBlockEditors[0].AvailableVenues;
        venues.Should().ContainSingle();
        venues[0].Id.Should().Be(VenueId, "Id 仍需保留用于配置存取");
        venues[0].Name.Should().Be("会议室A");
        venues[0].ToString().Should().Be("会议室A", "ComboBox 按 ToString 显示，不能落回 Id");

        await facade.DidNotReceive().ListVenueIdsAsync(Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task 选择器_按名称自然排序()
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.GetStrategiesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<StrategyDisplayInfo> { CreateStrategy() }));
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StudentDatasetInfo>>(
            [
                new StudentDatasetInfo { Id = "d10", Name = "高一10班" },
                new StudentDatasetInfo { Id = "d2", Name = "高一2班" },
            ]));
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<VenueSummary>>(
            [
                new VenueSummary("v10", "教室10"),
                new VenueSummary("v2", "教室2"),
            ]));
        facade.LoadStrategyDatasetConfigsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<StrategyDatasetConfig>()));

        var vm = CreateVm(facade);
        await vm.OnEnterAsync(CancellationToken.None);
        vm.SelectedStrategy = vm.Strategies[0];
        await WaitUntilAsync(() => vm.ConfigBlockEditors.Count > 0);

        var editor = vm.ConfigBlockEditors[0];
        editor.AvailableDatasets.Select(i => i.Name).Should().Equal("高一2班", "高一10班");
        editor.AvailableVenues.Select(i => i.Name).Should().Equal("教室2", "教室10");
    }

    [AvaloniaFact]
    public async Task 缓存命中_会场重命名后再次进入刷新显示()
    {
        var facade = CreateFacade("会议室A");
        var vm = CreateVm(facade);

        await vm.OnEnterAsync(CancellationToken.None);
        vm.SelectedStrategy = vm.Strategies[0];
        await WaitUntilAsync(() => vm.ConfigBlockEditors.Count > 0
            && vm.ConfigBlockEditors[0].AvailableVenues.Count > 0);

        // 模拟会场重命名后离开再进入（页面缓存命中，不走全量 LoadAsync）
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<VenueSummary>>([new VenueSummary(VenueId, "会议室B")]));
        await vm.OnLeaveAsync();
        await vm.OnEnterAsync(CancellationToken.None);

        await WaitUntilAsync(() => vm.ConfigBlockEditors[0].AvailableVenues[0].Name == "会议室B");
        vm.ConfigBlockEditors[0].AvailableVenues[0].Id.Should().Be(VenueId);
    }
}
