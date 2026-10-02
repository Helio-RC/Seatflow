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
/// 排座工作台会场列表 M6 启动优化测试：
/// 首屏只列 ID（避免启动时全量反序列化布局），选中 / 恢复工作区后回填真实名称，
/// 且集合项替换不会造成 SelectedVenue 丢失或二次加载。
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
        facade.ListVenueIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<string>>([VenueId]));
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
    public async Task 首屏会场列表_仅ID占位_不加载布局()
    {
        var layout = CreateLayout();
        var facade = CreateFacade(layout);
        var vm = CreateVm(facade);

        await vm.RefreshDataAsync();

        vm.VenueItems.Should().HaveCount(1);
        vm.VenueItems[0].Id.Should().Be(VenueId);
        vm.VenueItems[0].Name.Should().Be(VenueId, "首屏以 ID 占位，避免启动时全量反序列化");
        await facade.DidNotReceive().LoadVenueAsync(VenueId, Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task 选中会场_回填真实名称_选中不被清空_仅加载一次()
    {
        var facade = CreateFacade(CreateLayout());
        var vm = CreateVm(facade);
        await vm.RefreshDataAsync();

        vm.SelectedVenue = vm.VenueItems[0];
        await WaitUntilAsync(() => vm.VenueItems[0].Name == "演示教室");

        vm.VenueItems[0].Name.Should().Be("演示教室");
        vm.SelectedVenue.Should().NotBeNull("集合项替换后选中必须恢复");
        vm.SelectedVenue!.Id.Should().Be(VenueId);
        vm.SelectedVenue.Name.Should().Be("演示教室", "SelectedVenue 应同步为回填后的实例");
        await facade.Received(1).LoadVenueAsync(VenueId, Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task 恢复工作区_回填名称_不触发额外加载()
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
}
