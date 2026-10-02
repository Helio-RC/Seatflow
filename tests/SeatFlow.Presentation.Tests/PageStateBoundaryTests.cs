using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 页面状态边界测试（M6）：Loading / Error / Empty / Content 四态对应的可观察属性
/// （成员名单页为代表；画布四态见 <see cref="SeatingCanvasTests"/>）。
/// </summary>
public class PageStateBoundaryTests
{
    private static MemberManagementViewModel CreateVm() => new(
        Substitute.For<IApplicationFacade>(),
        Substitute.For<IFileService>(),
        NullDialogService.Instance,
        Substitute.For<IUrlOpener>(),
        new DialogGate(),
        new ShellLayoutService());

    [AvaloniaFact]
    public void 空态与内容态_属性互斥且通知联动()
    {
        var vm = CreateVm();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        // 初始 Empty
        vm.IsEmpty.Should().BeTrue();
        vm.HasData.Should().BeFalse();

        // 注入内容 → Content
        vm.SeedGuideData();

        vm.IsEmpty.Should().BeFalse();
        vm.HasData.Should().BeTrue();
        vm.StudentCount.Should().Be(6);
        changes.Should().Contain(nameof(MemberManagementViewModel.HasData));
    }

    [AvaloniaFact]
    public void 加载态_驱动_IsNotLoading_反向通知()
    {
        var vm = CreateVm();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.IsLoading = true;
        vm.IsNotLoading.Should().BeFalse();

        vm.IsLoading = false;
        vm.IsNotLoading.Should().BeTrue();
        changes.Should().Contain(nameof(MemberManagementViewModel.IsNotLoading));
    }

    [AvaloniaFact]
    public void 错误态_错误消息可观察()
    {
        var vm = CreateVm();

        vm.ErrorMessage = "导入失败：文件损坏";
        vm.ErrorMessage.Should().Be("导入失败：文件损坏");

        vm.ErrorMessage = string.Empty;
        vm.ErrorMessage.Should().BeEmpty();
    }
}
