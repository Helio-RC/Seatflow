using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// <see cref="IPageLifecycle"/> 状态机测试（M6）：
/// OnEnter 成功后 <see cref="IPageLifecycle.InitializationTask"/> 置位、OnLeave 换新未完成实例；
/// 数据加载取消 / 失败不置成功标志，下次进入自动重试。
/// </summary>
public class PageLifecycleTests
{
    // ═══════════════ MemberManagement ═══════════════

    private static MemberManagementViewModel CreateMemberVm(IApplicationFacade facade, IDialogService? dialog = null) => new(
        facade,
        Substitute.For<IFileService>(),
        dialog ?? NullDialogService.Instance,
        Substitute.For<IUrlOpener>(),
        new DialogGate(),
        new ShellLayoutService());

    [AvaloniaFact]
    public void 名单页_初始_InitializationTask_未完成()
    {
        var vm = CreateMemberVm(Substitute.For<IApplicationFacade>());

        vm.InitializationTask.IsCompleted.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task 名单页_OnEnter_成功后置位_且成功数据不重复加载()
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StudentDatasetInfo>>([]));
        var vm = CreateMemberVm(facade);

        await vm.OnEnterAsync(CancellationToken.None);

        vm.InitializationTask.IsCompleted.Should().BeTrue();

        // 已成功加载 → 再次进入不重复拉取
        await vm.OnEnterAsync(CancellationToken.None);
        await facade.Received(1).ListStudentDatasetsAsync(Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task 名单页_OnLeave_换新未完成信号()
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StudentDatasetInfo>>([]));
        var vm = CreateMemberVm(facade);
        await vm.OnEnterAsync(CancellationToken.None);

        await vm.OnLeaveAsync();

        vm.InitializationTask.IsCompleted.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task 名单页_加载失败不置位_下次进入重试()
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<StudentDatasetInfo>>(new InvalidOperationException("boom")));
        var vm = CreateMemberVm(facade);

        await vm.OnEnterAsync(CancellationToken.None);

        // 进入流程完成（finally 置位），但数据标志未置位
        vm.InitializationTask.IsCompleted.Should().BeTrue();

        await vm.OnEnterAsync(CancellationToken.None);
        await facade.Received(2).ListStudentDatasetsAsync(Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task 名单页_加载取消不置位_下次进入重试()
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<StudentDatasetInfo>>(new OperationCanceledException()));
        var vm = CreateMemberVm(facade);

        await vm.OnEnterAsync(CancellationToken.None);

        vm.InitializationTask.IsCompleted.Should().BeTrue();

        await vm.OnEnterAsync(CancellationToken.None);
        await facade.Received(2).ListStudentDatasetsAsync(Arg.Any<CancellationToken>());
    }

    // ═══════════════ SnapshotHistory ═══════════════

    private static SnapshotHistoryViewModel CreateSnapshotVm(IApplicationFacade facade, IDialogService? dialog = null) => new(
        facade,
        Substitute.For<INavigationService>(),
        new ShellLayoutService(),
        dialog ?? NullDialogService.Instance);

    [AvaloniaFact]
    public async Task 快照页_加载失败_弹错并保留重试标志()
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.ListVenueIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IEnumerable<string>>(new InvalidOperationException("boom")));
        var dialog = Substitute.For<IDialogService>();
        var vm = CreateSnapshotVm(facade, dialog);

        await vm.OnEnterAsync(CancellationToken.None);

        vm.InitializationTask.IsCompleted.Should().BeTrue();
        await dialog.Received(1).ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>());

        // 失败未置位 → 下次进入自动重试
        await vm.OnEnterAsync(CancellationToken.None);
        await facade.Received(2).ListVenueIdsAsync(Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task 快照页_手动刷新失败后_下次进入重试()
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.ListVenueIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<string>>([]));
        var vm = CreateSnapshotVm(facade);

        await vm.OnEnterAsync(CancellationToken.None);
        await facade.Received(1).ListVenueIdsAsync(Arg.Any<CancellationToken>());

        // 手动刷新失败：成功标志应回写为 false，允许下次进入自动重试
        facade.ListVenueIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IEnumerable<string>>(new InvalidOperationException("refresh failed")));
        await vm.LoadVenuesCommand.ExecuteAsync(null);

        facade.ListVenueIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<string>>([]));
        await vm.OnEnterAsync(CancellationToken.None);

        await facade.Received(3).ListVenueIdsAsync(Arg.Any<CancellationToken>());
    }
}
