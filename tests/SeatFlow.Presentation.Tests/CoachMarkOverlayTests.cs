using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Core.Telemetry;
using SeatFlow.Presentation.Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Presentation.Avalonia.Views;

namespace SeatFlow.Presentation.Tests;

/// <summary>交接卡命中：目标按钮保持可点击，遮罩/空白区域不拦截。</summary>
public class CoachMarkOverlayTests
{
    private static MainShellViewModel CreateShell(out OobeViewModel oobe)
    {
        var layout = new ShellLayoutService();
        var facade = Substitute.For<IApplicationFacade>();
        facade.LoadAppSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new AppSettings()));
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<StudentDatasetInfo>>([]));
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<VenueSummary>>([]));

        oobe = new OobeViewModel(
            facade,
            Substitute.For<IFileService>(),
            Substitute.For<ITelemetryService>(),
            Substitute.For<INavigationService>(),
            layout,
            new StartupRestoreState(),
            NullDialogService.Instance);

        return new MainShellViewModel(
            Substitute.For<INavigationService>(),
            oobe,
            layout,
            NullDialogService.Instance);
    }


    private static MainShellViewModel CreateShellWithSeating(ShellLayoutService layout)
    {
        var facade = Substitute.For<IApplicationFacade>();
        facade.LoadAppSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new AppSettings()));
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<StudentDatasetInfo>>([]));
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<VenueSummary>>([]));

        var oobe = new OobeViewModel(
            facade,
            Substitute.For<IFileService>(),
            Substitute.For<ITelemetryService>(),
            Substitute.For<INavigationService>(),
            layout,
            new StartupRestoreState(),
            NullDialogService.Instance);

        var shell = new MainShellViewModel(
            Substitute.For<INavigationService>(),
            oobe,
            layout,
            NullDialogService.Instance)
        {
            CurrentPage = PageKey.SeatingArrangement,
            CurrentViewModel = new SeatingArrangementViewModel(
                facade,
                Substitute.For<IFileService>(),
                Substitute.For<IArrangementCounterService>(),
                layout,
                new WelcomeCardViewModel(NullDialogService.Instance),
                Substitute.For<IServiceProvider>(),
                NullDialogService.Instance),
        };

        return shell;
    }

    [AvaloniaFact]
    public async Task 交接目标_手机档优先工具栏_桌面档优先欢迎卡按钮()
    {
        async Task<string?> ResolveAsync(double width, double height, LayoutTier tier)
        {
            var layout = new ShellLayoutService { Tier = tier };
            var shell = CreateShellWithSeating(layout);
            var view = new MainView { DataContext = shell };
            var window = new Window { Width = width, Height = height, Content = view };
            window.Show();
            shell.Oobe.HandoffState = OobeHandoffState.Prompt;
            shell.Oobe.IsHandoffVisible = true;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            window.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            var overlay = view.FindControl<CoachMarkOverlay>("OobeCoachMark");
            overlay.Should().NotBeNull();
            var target = overlay!.Target?.Name;
            window.Close();
            return target;
        }

        (await ResolveAsync(360, 200, LayoutTier.Phone))
            .Should().Be("GenerateButton", "手机档欢迎卡按钮在视口外时应高亮工具栏按钮");
        (await ResolveAsync(1200, 800, LayoutTier.Desktop))
            .Should().Be("WelcomeGenerateButton", "桌面档优先欢迎卡内生成按钮");
    }

    [AvaloniaFact]
    public void 目标按钮可命中_交接卡与遮罩不拦截()
    {
        var shell = CreateShell(out var oobe);
        oobe.HandoffState = OobeHandoffState.Prompt;
        oobe.IsHandoffVisible = true;

        var target = new Button
        {
            Width = 120,
            Height = 40,
            Margin = new Thickness(24),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        };
        var overlay = new CoachMarkOverlay { DataContext = shell };
        var root = new Grid { Width = 800, Height = 600 };
        root.Children.Add(target);
        root.Children.Add(overlay);

        var window = new Window { Width = 800, Height = 600, Content = root };
        window.Show();
        window.UpdateLayout();
        overlay.SetTarget(target);
        window.UpdateLayout();
        window.CaptureRenderedFrame();

        var point = target.TranslatePoint(new Point(20, 20), root);
        point.Should().NotBeNull();

        var hit = root.InputHitTest(point!.Value);
        hit.Should().NotBeNull();

        Visual? current = hit as Visual;
        while (current is not null && current is not Button)
            current = current.GetVisualParent();

        current.Should().BeSameAs(target, "交接卡覆盖层不得挡住高亮目标");

        window.Close();
    }
}
