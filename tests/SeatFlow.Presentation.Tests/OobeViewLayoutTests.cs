using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FluentAssertions;
using NSubstitute;
using ReactiveUI;
using ReactiveUI.Primitives;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Core.Telemetry;
using SeatFlow.Presentation.Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Presentation.Avalonia.Views;

namespace SeatFlow.Presentation.Tests;

/// <summary>OOBE 视图三档宽度可渲染，且全屏/隐藏状态跟随 ViewModel。</summary>
public class OobeViewLayoutTests
{
    private static OobeViewModel CreateVm(ShellLayoutService? layout = null)
    {
        var settings = new AppSettings
        {
            IsFirstLaunch = true,
            Telemetry = new TelemetryConfig { Enabled = true, ConsentShown = false },
        };
        var facade = Substitute.For<IApplicationFacade>();
        facade.LoadAppSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(settings));
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<StudentDatasetInfo>>([]));
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<VenueSummary>>([]));

        return new OobeViewModel(
            facade,
            Substitute.For<IFileService>(),
            Substitute.For<ITelemetryService>(),
            Substitute.For<INavigationService>(),
            layout ?? new ShellLayoutService(),
            new StartupRestoreState(),
            NullDialogService.Instance);
    }

    [AvaloniaFact]
    public async Task 三档宽度_首启欢迎屏可渲染且不溢出()
    {
        foreach (var (width, height) in new[] { (1440, 900), (760, 800), (360, 640) })
        {
            var vm = CreateVm();
            var view = new OobeView { DataContext = vm };
            var window = new Window { Width = width, Height = height, Content = view };
            window.Show();
            await vm.StartAsync();
            window.UpdateLayout();

            view.IsEffectivelyVisible.Should().BeTrue();
            view.Bounds.Width.Should().BeLessThanOrEqualTo(width + 0.5);
            var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            texts.Should().Contain(Resources.Oobe_WelcomeTitle);
            texts.Should().Contain(Resources.Oobe_WelcomePoint1Desc, "欢迎屏三步课桌应渲染");
            texts.Should().Contain(Resources.Oobe_WelcomePoint2Desc, "欢迎屏三步课桌应渲染");
            texts.Should().Contain(Resources.Oobe_WelcomePoint3Desc, "欢迎屏三步课桌应渲染");
            view.GetVisualDescendants()
                .OfType<Button>()
                .Count(b => b.IsEffectivelyVisible
                    && string.Equals(b.Content as string, Resources.Oobe_SkipAll, StringComparison.Ordinal))
                .Should()
                .Be(1, "欢迎屏只保留主按钮旁的跳过设置");

            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task 名单步骤_共享表格渲染表头与行()
    {
        var vm = CreateVm();
        var view = new OobeView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        await vm.StartAsync(isRestart: true);

        vm.StartBlankRosterCommand.Execute().SubscribeSafe(_ => { });
        vm.NewStudent.Name = "张三";
        vm.AddNewStudentCommand.Execute().SubscribeSafe(_ => { });
        vm.Step = OobeStep.Roster;
        window.UpdateLayout();

        var table = view.GetVisualDescendants().OfType<StudentRosterTable>().Single();
        var texts = table.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.Should().Contain(Resources.Member_Name, "表头与人员管理页共用");
        texts.Should().Contain("张三", "名单行应渲染");
        table.GetVisualDescendants()
            .OfType<TextBox>()
            .Any(t => t.PlaceholderText == Resources.Member_NewRowHint)
            .Should().BeTrue("底部新增行应存在");

        window.Close();
    }


    [AvaloniaFact]
    public async Task 手机与紧凑档_侧栏折叠_名单与会场单栏()
    {
        foreach (var (width, tier) in new[] { (360, LayoutTier.Phone), (760, LayoutTier.Compact) })
        {
            var layout = new ShellLayoutService { Tier = tier };
            var vm = CreateVm(layout);
            var view = new OobeView { DataContext = vm };
            var window = new Window { Width = width, Height = 800, Content = view };
            window.Show();
            await vm.StartAsync(isRestart: true);
            window.UpdateLayout();

            var rail = view.FindControl<Border>("OobeRail");
            rail.Should().NotBeNull();
            rail!.IsEffectivelyVisible.Should().Be(tier != LayoutTier.Phone,
                "手机档侧栏应折叠且不再占用固定 300px 列");

            var roster = view.FindControl<ResponsiveSplitPanel>("RosterSplitPanel");
            roster.Should().NotBeNull();
            roster!.IsNarrow.Should().BeTrue();
            roster.Bounds.Width.Should().BeGreaterThan(0);
            roster.Bounds.Width.Should().BeLessThanOrEqualTo(width + 0.5);
            roster.Children.Should().HaveCount(2);
            roster.Children[1].Bounds.Y.Should().BeGreaterThan(roster.Children[0].Bounds.Y,
                "窄屏名单区应纵向堆叠");

            vm.Step = OobeStep.Venue;
            window.UpdateLayout();

            var editor = view.FindControl<StackPanel>("VenueEditorPanel");
            editor.Should().NotBeNull();
            var preview = view.FindControl<Border>("VenuePreviewCard");
            preview.Should().NotBeNull();
            preview!.IsEffectivelyVisible.Should().BeTrue();
            preview.Bounds.Y.Should().BeGreaterThanOrEqualTo(editor!.Bounds.Bottom,
                "会场预览应位于编辑区下方");
            preview.Bounds.Width.Should().BeLessThanOrEqualTo(width + 0.5);

            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task 桌面档_名单保持两栏_会场预览全宽()
    {
        var layout = new ShellLayoutService { Tier = LayoutTier.Desktop };
        var vm = CreateVm(layout);
        var view = new OobeView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        await vm.StartAsync(isRestart: true);
        window.UpdateLayout();

        var roster = view.FindControl<ResponsiveSplitPanel>("RosterSplitPanel")!;
        roster.IsNarrow.Should().BeFalse();
        roster.Children[1].Bounds.X.Should().BeGreaterThan(roster.Children[0].Bounds.X,
            "桌面档名单区应左右分栏");

        vm.Step = OobeStep.Venue;
        window.UpdateLayout();
        var editor = view.FindControl<StackPanel>("VenueEditorPanel")!;
        var preview = view.FindControl<Border>("VenuePreviewCard")!;
        preview.Bounds.Y.Should().BeGreaterThanOrEqualTo(editor.Bounds.Bottom,
            "桌面档会场预览应位于编辑区下方");
        preview.Bounds.Width.Should().BeGreaterThan(600, "桌面档会场预览应为全宽卡片");

        window.Close();
    }

    [AvaloniaFact]
    public async Task 隐藏与步骤切换_面板可见性跟随()
    {
        var vm = CreateVm();
        var view = new OobeView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        await vm.StartAsync();
        window.UpdateLayout();

        view.IsEffectivelyVisible.Should().BeTrue();

        vm.Step = OobeStep.Roster;
        window.UpdateLayout();
        view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Any(t => t.Text == Resources.Oobe_RosterTitle)
            .Should().BeTrue();

        vm.Step = OobeStep.Venue;
        window.UpdateLayout();
        view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Any(t => t.Text == Resources.Oobe_VenueTitle)
            .Should().BeTrue();

        vm.IsVisible = false;
        window.UpdateLayout();
        view.IsEffectivelyVisible.Should().BeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task 保存中_步骤内容禁用防止修改丢失()
    {
        var vm = CreateVm();
        var view = new OobeView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        await vm.StartAsync(isRestart: true);
        window.UpdateLayout();

        var content = view.FindControl<StackPanel>("OobeContent");
        content.Should().NotBeNull();
        content!.IsEffectivelyEnabled.Should().BeTrue();

        vm.IsBusy = true;
        window.UpdateLayout();
        content.IsEffectivelyEnabled.Should().BeFalse("保存期间禁止编辑表单，避免未保存修改丢失");

        vm.IsBusy = false;
        window.UpdateLayout();
        content.IsEffectivelyEnabled.Should().BeTrue();

        window.Close();
    }
}
