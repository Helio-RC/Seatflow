using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
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
    };

    private static IApplicationFacade CreateFacade(ClassroomLayoutDefinition layout)
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<VenueSummary>>([new(VenueId, layout.Name)]));
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
}
