using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// <see cref="IGuideSeedTarget"/> 的 seed / clear 等价性测试（M6）：
/// 各页面自行注入并恢复演示数据，未注入时清理无副作用（幂等）。
/// </summary>
public class GuideSeedTargetTests
{
    private static MemberManagementViewModel CreateMemberVm() => new(
        Substitute.For<IApplicationFacade>(),
        Substitute.For<IFileService>(),
        NullDialogService.Instance,
        Substitute.For<IUrlOpener>(),
        new DialogGate(),
        new ShellLayoutService());

    private static SnapshotHistoryViewModel CreateSnapshotVm() => new(
        Substitute.For<IApplicationFacade>(),
        Substitute.For<INavigationService>(),
        new ShellLayoutService(),
        NullDialogService.Instance);

    private static SeatingArrangementViewModel CreateSeatingVm(
        IApplicationFacade? facade = null,
        IArrangementCounterService? counter = null)
    {
        var resolvedFacade = facade ?? Substitute.For<IApplicationFacade>();
        var resolvedCounter = counter ?? Substitute.For<IArrangementCounterService>();
        return new SeatingArrangementViewModel(
            resolvedFacade,
            Substitute.For<IFileService>(),
            resolvedCounter,
            new ShellLayoutService(),
            new WelcomeCardViewModel(NullDialogService.Instance),
            Substitute.For<IServiceProvider>(),
            NullDialogService.Instance);
    }

    // ═══════════════ Member：首次使用分支 ═══════════════

    [AvaloniaFact]
    public void 名单页_首次使用_注入演示后清理恢复为空()
    {
        var vm = CreateMemberVm();
        vm.IsEmpty.Should().BeTrue();

        vm.SeedGuideData();

        vm.Students.Should().HaveCount(6);
        vm.IsEmpty.Should().BeFalse();
        vm.SavedDatasets.Should().Contain(d => d.Id == "guide-demo-ds");
        vm.SelectedDataset!.Id.Should().Be("guide-demo-ds");

        vm.ClearGuideData();

        vm.Students.Should().BeEmpty();
        vm.IsEmpty.Should().BeTrue();
        vm.SavedDatasets.Should().NotContain(d => d.Id == "guide-demo-ds");
        vm.SelectedDataset.Should().BeNull();
    }

    // ═══════════════ Member：已有用户数据分支 ═══════════════

    [AvaloniaFact]
    public void 名单页_已有用户数据_注入不覆盖_清理不触碰()
    {
        var vm = CreateMemberVm();
        vm.Students.Add(new StudentRowViewModel(new Student { Name = "真实学生" }));
        vm.SavedDatasets.Add(new StudentDatasetInfo { Id = "real-ds", Name = "真实数据集" });

        vm.SeedGuideData();

        // 已有用户数据：跳过注入（不覆盖学生、不追加演示数据集）
        vm.Students.Should().HaveCount(1);
        vm.Students[0].Name.Should().Be("真实学生");
        vm.SavedDatasets.Should().HaveCount(1);
        vm.SavedDatasets.Should().NotContain(d => d.Id == "guide-demo-ds");

        vm.ClearGuideData();

        vm.Students.Should().HaveCount(1);
        vm.Students[0].Name.Should().Be("真实学生");
        vm.SavedDatasets.Should().Contain(d => d.Id == "real-ds");
        vm.SavedDatasets.Should().NotContain(d => d.Id == "guide-demo-ds");
    }

    // ═══════════════ Snapshot：注入标志与幂等清理 ═══════════════

    [AvaloniaFact]
    public void 快照页_未注入时_清理不触碰用户浏览状态()
    {
        var vm = CreateSnapshotVm();
        vm.Snapshots.Add(new SeatingSnapshot { Id = "user-snap", Description = "用户快照" });

        vm.ClearGuideData();

        vm.Snapshots.Should().HaveCount(1);
        vm.Snapshots[0].Id.Should().Be("user-snap");
    }

    [AvaloniaFact]
    public void 快照页_注入演示后清理清空_并标记数据失效()
    {
        var vm = CreateSnapshotVm();

        vm.SeedGuideData();

        vm.Venues.Should().HaveCount(1);
        vm.Snapshots.Should().HaveCount(1);

        vm.ClearGuideData();

        vm.Venues.Should().BeEmpty();
        vm.Snapshots.Should().BeEmpty();

        // 幂等：再次清理无副作用
        vm.ClearGuideData();
        vm.Venues.Should().BeEmpty();
    }

    // ═══════════════ Seating：演示后画布快照已生成 ═══════════════

    [AvaloniaFact]
    public void 排座页_演示注入后生成画布快照_清理清空工作区()
    {
        var vm = CreateSeatingVm();

        vm.SeedGuideData();

        vm.SeatItems.Should().HaveCount(12);
        vm.HasGenerated.Should().BeTrue();
        vm.TotalSeats.Should().Be(12);
        vm.AssignedSeats.Should().Be(6);
        // M5 修复点：Seed 后必须调用 UpdateCanvasSnapshot，画布才能渲染演示座位
        vm.CanvasSnapshot.Should().NotBeNull();
        vm.CanvasSnapshot!.Seats.Should().HaveCount(12);

        vm.ClearGuideData();

        vm.SeatItems.Should().BeEmpty();
        vm.HasGenerated.Should().BeFalse();
    }
}
