using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 排座工作台会场列表：
/// 摘要（ID + 名称）直接展示、不反序列化布局；选中 / 恢复工作区按需加载布局；
/// 重新进入页面轻量刷新摘要（会场重命名/增删后名称即时可见）。
/// </summary>
public class SeatingVenueLoadingTests
{
    private const string VenueId = "demo-v1";

    private static ClassroomLayoutDefinition CreateLayout() => new()
    {
        Id = VenueId,
        Name = "演示教室",
        Seats = [new GridSeat { Row = 1, Column = 1 }, new GridSeat { Row = 1, Column = 2 }],
    };

    private static IApplicationFacade CreateFacade(ClassroomLayoutDefinition layout)
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<VenueSummary>>(
                [new VenueSummary(VenueId, layout.Name)
                {
                    LayoutType = layout.LayoutType,
                    SeatCount = layout.Seats.Count,
                }]));
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StudentDatasetInfo>>([]));
        facade.LoadAppSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AppSettings()));
        facade.GetStrategiesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<StrategyDisplayInfo>()));
        facade.LoadVenueAsync(VenueId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ClassroomLayoutDefinition?>(layout));
        return facade;
    }

    private static SeatingArrangementViewModel CreateVm(IApplicationFacade facade) => new(
        facade,
        Substitute.For<IFileService>(),
        Substitute.For<IArrangementCounterService>(),
        new ShellLayoutService(),
        new WelcomeCardViewModel(NullDialogService.Instance),
        Substitute.For<IServiceProvider>(),
        NullDialogService.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var started = Environment.TickCount64;
        while (!condition() && Environment.TickCount64 - started < timeoutMs)
            await Task.Delay(15);
    }

    [AvaloniaFact]
    public async Task 首屏会场列表_直接显示名称_不加载布局()
    {
        var facade = CreateFacade(CreateLayout());
        var vm = CreateVm(facade);

        await vm.RefreshDataAsync();

        vm.VenueItems.Should().HaveCount(1);
        vm.VenueItems[0].Id.Should().Be(VenueId);
        vm.VenueItems[0].Name.Should().Be("演示教室", "摘要应携带名称，首屏不再以 ID 占位");
        await facade.DidNotReceive().LoadVenueAsync(VenueId, Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task 选中会场_加载布局一次_名称保持不变()
    {
        var facade = CreateFacade(CreateLayout());
        var vm = CreateVm(facade);
        await vm.RefreshDataAsync();

        vm.SelectedVenue = vm.VenueItems[0];
        await WaitUntilAsync(() => vm.StatusMessage.Contains("演示教室"));

        vm.VenueItems[0].Name.Should().Be("演示教室");
        vm.SelectedVenue.Should().NotBeNull();
        vm.SelectedVenue!.Id.Should().Be(VenueId);
        await facade.Received(1).LoadVenueAsync(VenueId, Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task 恢复工作区_名称直接可用_不触发额外加载()
    {
        var layout = CreateLayout();
        var facade = CreateFacade(layout);
        var workspace = new SeatingWorkspace([], []);
        facade.GetCurrentWorkspaceAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SeatingWorkspace?>(workspace));
        facade.GetCurrentLayoutAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ClassroomLayoutDefinition?>(layout));
        var vm = CreateVm(facade);

        await vm.RefreshDataAsync();

        vm.VenueItems[0].Name.Should().Be("演示教室");
        vm.SelectedVenue.Should().NotBeNull();
        vm.SelectedVenue!.Id.Should().Be(VenueId);
        vm.HasGenerated.Should().BeTrue("恢复工作区后进入已有结果状态");
        await facade.DidNotReceive().LoadVenueAsync(VenueId, Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task 重新进入页面_会场重命名后名称刷新_选中保留且不重复加载()
    {
        var facade = CreateFacade(CreateLayout());
        var vm = CreateVm(facade);
        await vm.RefreshDataAsync();
        vm.SelectedVenue = vm.VenueItems[0];
        await WaitUntilAsync(() => vm.StatusMessage.Contains("演示教室"));

        // 会场页重命名后，摘要名称变化
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<VenueSummary>>([new(VenueId, "新名字")]));

        await vm.RefreshDataAsync();

        vm.VenueItems.Should().HaveCount(1);
        vm.VenueItems[0].Name.Should().Be("新名字", "重新进入页面应轻量刷新会场名称");
        vm.SelectedVenue.Should().NotBeNull("集合替换后按 Id 保留选中");
        vm.SelectedVenue!.Id.Should().Be(VenueId);
        await facade.Received(1).LoadVenueAsync(VenueId, Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task 会场列表_按名称自然排序()
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<VenueSummary>>(
            [
                new VenueSummary("v10", "教室10"),
                new VenueSummary("v2", "教室2"),
            ]));
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StudentDatasetInfo>>([]));
        facade.LoadAppSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AppSettings()));
        facade.GetStrategiesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<StrategyDisplayInfo>()));

        var vm = CreateVm(facade);
        await vm.RefreshDataAsync();

        vm.VenueItems.Select(v => v.Name).Should().Equal("教室2", "教室10");
    }

    [AvaloniaFact]
    public async Task 重新进入页面_名单新增与重命名后自动刷新_选中保留()
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<VenueSummary>>([]));
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult<IReadOnlyList<StudentDatasetInfo>>(
                    [new StudentDatasetInfo { Id = "ds-1", Name = "班级2", StudentCount = 3 }]),
                _ => Task.FromResult<IReadOnlyList<StudentDatasetInfo>>(
                [
                    new StudentDatasetInfo { Id = "ds-1", Name = "班级2(改)", StudentCount = 5 },
                    new StudentDatasetInfo { Id = "ds-2", Name = "班级10", StudentCount = 8 },
                ]));
        facade.LoadAppSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AppSettings()));
        var vm = CreateVm(facade);

        await vm.RefreshDataAsync();
        vm.DatasetItems.Should().ContainSingle();
        vm.SelectedDataset = vm.DatasetItems[0];

        // 人员页新增名单 / 重命名后重新进入排座页
        await vm.RefreshDataAsync();

        vm.DatasetItems.Select(d => d.Name).Should().Equal(
            new[] { "班级2(改)", "班级10" },
            "名单列表应自动刷新并按名称自然排序");
        vm.SelectedDataset.Should().NotBeNull("按 Id 保留选中项");
        vm.SelectedDataset!.Id.Should().Be("ds-1");
    }

    [AvaloniaFact]
    public async Task 会场摘要_携带布局类型与座位数()
    {
        var facade = CreateFacade(CreateLayout());
        var vm = CreateVm(facade);

        await vm.RefreshDataAsync();

        var item = vm.VenueItems.Should().ContainSingle().Subject;
        item.SeatCount.Should().Be(2);
        item.LayoutType.Should().Be(LayoutType.Grid);
    }
}
