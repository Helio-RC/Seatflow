using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Presentation.Avalonia.Views;

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
            new ShellLayoutService(),
            NullDialogService.Instance);

        await vm.OnEnterAsync(CancellationToken.None);

        vm.VenueItems.Select(v => v.Name).Should().Equal("教室2", "教室10");
    }

    [AvaloniaFact]
    public async Task 会场列表_显示座位数与布局类型()
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<VenueSummary>>(
                [new VenueSummary("v1", "一号教室") { LayoutType = LayoutType.Grid, SeatCount = 40 }]));

        var vm = new VenueConfigurationViewModel(
            facade,
            Substitute.For<IFileService>(),
            Substitute.For<IDialogGate>(),
            new ShellLayoutService(),
            NullDialogService.Instance);
        var view = new VenueConfigurationView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        await vm.OnEnterAsync(CancellationToken.None);
        window.UpdateLayout();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.Should().Contain(string.Format(Resources.Venue_SeatCountFmt, 40), "列表项应展示座位数");
        texts.Should().Contain(Resources.Venue_GridType, "列表项应展示布局类型");

        window.Close();
    }
}
