using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>「会场与布局」页会场列表按名称自然排序（而非仓储的 Id 顺序）。</summary>
public class VenueListSortingTests
{
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

        var vm = new VenueConfigurationViewModel(
            facade,
            Substitute.For<IFileService>(),
            Substitute.For<IDialogGate>(),
            NullDialogService.Instance);

        await vm.OnEnterAsync(CancellationToken.None);

        vm.VenueItems.Select(v => v.Name).Should().Equal("教室2", "教室10");
    }
}
