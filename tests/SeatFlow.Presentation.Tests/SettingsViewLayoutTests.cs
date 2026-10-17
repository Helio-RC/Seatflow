using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Telemetry;
using SeatFlow.Presentation.Avalonia.Behaviors;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Presentation.Avalonia.Views;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 设置页卡片网格布局回归测试：网页端通过 <c>IsVisible</c> 隐藏「存储 / 更新」卡片后，
/// 卡片必须重排成整齐的等列网格，而非在原槽位留下空洞（错位）。
/// 根因：使用 <c>WrapPanel.ItemWidth</c> 时，隐藏子项仍被分配固定宽度、占用列槽位；
/// 修复改为逐卡片设置 <c>Width</c>，隐藏卡片 DesiredSize 归零后不再占位。
/// </summary>
public class SettingsViewLayoutTests
{
    private const double CardSpacing = 16;

    private static (SettingsView View, ShellLayoutService Layout) CreateView()
    {
        var layout = new ShellLayoutService();
        var vm = new SettingsViewModel(
            Substitute.For<IApplicationFacade>(),
            NullDialogService.Instance,
            Substitute.For<IOnboardingService>(),
            Substitute.For<IFileService>(),
            Substitute.For<ITelemetryService>(),
            Substitute.For<IUpdateService>(),
            Substitute.For<IUrlOpener>(),
            Substitute.For<IServiceProvider>(),
            new KeyboardShortcutHandler(),
            new DialogGate(),
            layout);
        return (new SettingsView { DataContext = vm }, layout);
    }

    /// <summary>模拟网页端隐藏「存储」「更新」两张卡片（按 x:Name 定位，避免依赖卡片顺序）。</summary>
    private static void HidePlatformCards(SettingsView view)
    {
        view.FindControl<Border>("StorageCard")!.IsVisible = false;
        view.FindControl<Border>("UpdateCard")!.IsVisible = false;
    }

    /// <summary>按 Bounds.Y 分组断言行内可见卡片数量。</summary>
    private static void AssertRows(WrapPanel panel, params int[] expected)
    {
        var rows = panel.Children
            .Where(c => c.IsVisible)
            .GroupBy(c => Math.Round(c.Bounds.Y, 1))
            .OrderBy(g => g.Key)
            .Select(g => g.Count())
            .ToList();

        rows.Should().Equal(expected);
    }

    private static void AssertVisibleWidths(WrapPanel panel, double expected)
    {
        foreach (var child in panel.Children.Where(c => c.IsVisible))
            child.Bounds.Width.Should().BeApproximately(expected, 1.0);
    }

    [AvaloniaFact]
    public void 隐藏平台卡片后_可见卡片按两列整齐重排_无空洞()
    {
        var (view, _) = CreateView();
        var window = new Window { Width = 1400, Height = 2000, Content = view };
        try
        {
            window.Show();
            window.UpdateLayout();

            var panel = view.FindControl<WrapPanel>("SettingsCards");
            panel.Should().NotBeNull();
            var cards = panel!;
            cards.Children.Count.Should().Be(8, "设置页共 8 张卡片");
            double.IsNaN(cards.ItemWidth).Should().BeTrue("宽度应由逐卡片 Width 控制，而非 WrapPanel.ItemWidth");

            HidePlatformCards(view);
            window.UpdateLayout();

            // 6 张可见卡片应在两列布局下排成 3 行，行内不应有隐藏卡片留下的空洞
            AssertRows(cards, 2, 2, 2);
            AssertVisibleWidths(cards, (cards.Bounds.Width - CardSpacing) / 2);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void 窄屏单列占满宽度_且随布局档位运行时切换重排()
    {
        var (view, layout) = CreateView();
        var window = new Window { Width = 700, Height = 2000, Content = view };
        try
        {
            window.Show();
            window.UpdateLayout();

            var panel = view.FindControl<WrapPanel>("SettingsCards");
            panel.Should().NotBeNull();
            var cards = panel!;
            HidePlatformCards(view);
            window.UpdateLayout();

            // 初始为桌面档：两列等宽
            AssertRows(cards, 2, 2, 2);
            AssertVisibleWidths(cards, (cards.Bounds.Width - CardSpacing) / 2);

            // 运行时切到紧凑/窄屏档：View 直接响应 CardColumns 变更 → 单列占满可用宽度
            layout.Tier = LayoutTier.Compact;
            window.UpdateLayout();
            AssertRows(cards, 1, 1, 1, 1, 1, 1);
            AssertVisibleWidths(cards, cards.Bounds.Width);

            // 切回桌面档：恢复两列
            layout.Tier = LayoutTier.Desktop;
            window.UpdateLayout();
            AssertRows(cards, 2, 2, 2);
        }
        finally
        {
            window.Close();
        }
    }
}
