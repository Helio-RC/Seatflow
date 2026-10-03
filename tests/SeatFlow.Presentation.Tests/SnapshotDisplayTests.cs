using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>快照详情：关联会场/人员数据集显示「名称（id）」，缺失时优雅回退。</summary>
public class SnapshotDisplayTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var started = Environment.TickCount64;
        while (!condition() && Environment.TickCount64 - started < timeoutMs)
            await Task.Delay(15);

        if (!condition())
            throw new TimeoutException($"等待条件超时（{timeoutMs}ms）");
    }

    [Theory]
    [InlineData(null, null, "—")]
    [InlineData("", "名称", "—")]
    [InlineData("v1", null, "v1")]
    [InlineData("v1", "", "v1")]
    [InlineData("v1", "会议室A", "会议室A（v1）")]
    public void 格式化_名称与ID(string? id, string? name, string expected)
        => SnapshotHistoryViewModel.FormatSnapshotNameId(id, name).Should().Be(expected);

    [AvaloniaFact]
    public async Task 快照详情_显示会场与数据集名称加ID()
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.ListVenueIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<string>>(["v1"]));
        facade.LoadVenueAsync("v1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ClassroomLayoutDefinition?>(new ClassroomLayoutDefinition
            {
                Id = "v1",
                Name = "测试教室",
                LayoutType = LayoutType.Grid,
            }));
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StudentDatasetInfo>>(
                [new StudentDatasetInfo { Id = "d1", Name = "测试数据" }]));
        facade.LoadStudentDatasetAsync("d1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<List<Student>?>([]));
        facade.LoadAppSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AppSettings()));
        facade.GetSnapshotsAsync("v1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SeatingSnapshot>>(
                [new SeatingSnapshot { Id = "s1", LayoutId = "v1", DatasetId = "d1" }]));

        var vm = new SnapshotHistoryViewModel(
            facade,
            Substitute.For<INavigationService>(),
            new ShellLayoutService(),
            NullDialogService.Instance);

        await vm.OnEnterAsync(CancellationToken.None);
        vm.SelectedVenue = vm.Venues[0];
        await WaitUntilAsync(() => vm.SelectedSnapshot is not null);

        vm.SelectedSnapshotVenueDisplay.Should().Be("测试教室（v1）");
        vm.SelectedSnapshotDatasetDisplay.Should().Be("测试数据（d1）");
    }
}
