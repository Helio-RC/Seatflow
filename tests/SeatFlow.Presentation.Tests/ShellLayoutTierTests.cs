using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Presentation.Avalonia.Views;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 响应式三档布局（M7）：断点解析、外壳档位行为、面板三态（桌面内联 / 紧凑抽屉 / 手机全屏）、
/// 外壳与排座工作台的手机档无溢出不变量。
/// </summary>
public class ShellLayoutTierTests
{
    private static MainShellViewModel CreateShell(ShellLayoutService layout, INavigationService? navigation = null) => new(
        navigation ?? Substitute.For<INavigationService>(),
        Substitute.For<IApplicationFacade>(),
        Substitute.For<IOnboardingService>(),
        layout,
        NullDialogService.Instance);

    private static SeatingArrangementViewModel CreateSeatingVm(ShellLayoutService layout) => new(
        Substitute.For<IApplicationFacade>(),
        Substitute.For<IFileService>(),
        Substitute.For<IArrangementCounterService>(),
        layout,
        new WelcomeCardViewModel(NullDialogService.Instance),
        Substitute.For<IServiceProvider>(),
        NullDialogService.Instance);

    // ═══════════════ 断点解析 ═══════════════

    [Theory]
    [InlineData(360, LayoutTier.Phone)]
    [InlineData(600, LayoutTier.Phone)]
    [InlineData(601, LayoutTier.Compact)]
    [InlineData(900, LayoutTier.Compact)]
    [InlineData(901, LayoutTier.Desktop)]
    [InlineData(1600, LayoutTier.Desktop)]
    public void 断点解析_按宽度返回档位(double width, LayoutTier expected)
        => LayoutBreakpoints.Resolve(width).Should().Be(expected);

    [Fact]
    public void 服务_档位变化_通知派生属性()
    {
        var layout = new ShellLayoutService();
        var changes = new List<string?>();
        layout.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        layout.Tier = LayoutTier.Phone;

        changes.Should().Contain(nameof(IShellLayoutService.IsNarrow));
        changes.Should().Contain(nameof(IShellLayoutService.IsPhone));
        layout.IsNarrow.Should().BeTrue();
        layout.IsPhone.Should().BeTrue();

        layout.Tier = LayoutTier.Desktop;
        layout.IsNarrow.Should().BeFalse();
        layout.IsPhone.Should().BeFalse();
    }

    // ═══════════════ 外壳档位行为 ═══════════════

    [Fact]
    public void 外壳_零宽度_忽略不误判()
    {
        var layout = new ShellLayoutService();
        var vm = CreateShell(layout);

        vm.OnWindowWidthChanged(0);

        layout.Tier.Should().Be(LayoutTier.Desktop, "初始布局 Bounds=0 不应误判为手机档");
    }

    [Fact]
    public void 外壳_手机档_顶栏隐藏_Tab显示_导航抽屉关闭()
    {
        var layout = new ShellLayoutService();
        var vm = CreateShell(layout);
        vm.OnWindowWidthChanged(750);
        vm.ToggleNavDrawerCommand.Execute(null);
        vm.IsNavDrawerOpen.Should().BeTrue();

        vm.OnWindowWidthChanged(390);

        layout.Tier.Should().Be(LayoutTier.Phone);
        vm.IsCompactTopBarVisible.Should().BeFalse();
        vm.IsPhoneTabBarVisible.Should().BeTrue();
        vm.IsNavDrawerVisible.Should().BeFalse();
        vm.IsMaskVisible.Should().BeFalse();
        vm.IsNavDrawerOpen.Should().BeFalse("切到手机档应强制收起导航抽屉");
    }

    [Fact]
    public void 外壳_紧凑档_顶栏与左抽屉生效()
    {
        var layout = new ShellLayoutService();
        var vm = CreateShell(layout);

        vm.OnWindowWidthChanged(750);

        layout.Tier.Should().Be(LayoutTier.Compact);
        vm.IsCompactTopBarVisible.Should().BeTrue();
        vm.IsPhoneTabBarVisible.Should().BeFalse();
        vm.IsNavDrawerVisible.Should().BeFalse("抽屉未展开");
        vm.IsMaskVisible.Should().BeFalse();

        vm.ToggleNavDrawerCommand.Execute(null);

        vm.IsNavDrawerVisible.Should().BeTrue();
        vm.IsMaskVisible.Should().BeTrue();
        vm.RailDisplayWidth.Should().Be(280);
    }

    [Fact]
    public void 外壳_桌面档_侧栏内联_Tab与顶栏隐藏()
    {
        var layout = new ShellLayoutService();
        var vm = CreateShell(layout);

        vm.OnWindowWidthChanged(1200);

        layout.Tier.Should().Be(LayoutTier.Desktop);
        vm.IsNavDrawerVisible.Should().BeTrue();
        vm.IsCompactTopBarVisible.Should().BeFalse();
        vm.IsPhoneTabBarVisible.Should().BeFalse();
        vm.RailDisplayWidth.Should().Be(172);
    }

    [Fact]
    public void 外壳_页面开关_默认全启用_更多Tab可见()
    {
        var layout = new ShellLayoutService();
        var vm = CreateShell(layout);

        // page_navigation.json 是嵌入资源（不可注入），此处仅验证默认配置路径；
        // 禁用页过滤逻辑由 IsPageEnabled + XAML 绑定一一对应（评审已核验）。
        vm.IsSeatingNavEnabled.Should().BeTrue();
        vm.IsMoreTabVisible.Should().BeTrue("page_navigation.json 默认全启用");
    }

    // ═══════════════ SideDrawerState 三态 ═══════════════

    [Fact]
    public void 面板三态_桌面内联_紧凑抽屉_手机全屏_回桌面收起()
    {
        var layout = new ShellLayoutService();
        var panel = new SideDrawerState(layout, 0, 240);

        // 桌面：内联常驻
        panel.Visible.Should().BeTrue();
        panel.Width.Should().Be(240);
        panel.Column.Should().Be(0);
        panel.MaskVisible.Should().BeFalse();
        panel.IsFullScreen.Should().BeFalse();

        // 紧凑：右抽屉 + 遮罩
        layout.Tier = LayoutTier.Compact;
        panel.Width.Should().Be(320);
        panel.Column.Should().Be(1);
        panel.Visible.Should().BeFalse("抽屉未打开");
        panel.IsOpen = true;
        panel.Visible.Should().BeTrue();
        panel.MaskVisible.Should().BeTrue();
        panel.Alignment.Should().Be(global::Avalonia.Layout.HorizontalAlignment.Right);

        // 手机：全屏覆盖、无遮罩
        layout.Tier = LayoutTier.Phone;
        panel.Visible.Should().BeTrue("保持打开");
        double.IsNaN(panel.Width).Should().BeTrue("手机档全宽拉伸");
        panel.MaskVisible.Should().BeFalse();
        panel.IsFullScreen.Should().BeTrue();
        panel.Alignment.Should().Be(global::Avalonia.Layout.HorizontalAlignment.Stretch);

        // 回桌面：自动收起
        layout.Tier = LayoutTier.Desktop;
        panel.IsOpen.Should().BeFalse();
        panel.Width.Should().Be(240);
    }

    // ═══════════════ 排座工作台面板属性 ═══════════════

    [Fact]
    public void 排座工作台_手机档_面板全屏无遮罩_标题保留()
    {
        var layout = new ShellLayoutService();
        var vm = CreateSeatingVm(layout);

        layout.Tier = LayoutTier.Phone;

        double.IsNaN(vm.PickerPanelWidth).Should().BeTrue();
        double.IsNaN(vm.InspectorPanelWidth).Should().BeTrue();
        vm.DrawerMaskVisible.Should().BeFalse();
        vm.IsFullScreen.Should().BeTrue();
        vm.TitleVisible.Should().BeTrue("手机档无外壳顶栏，页面标题需显示");
        vm.CommandTextVisible.Should().BeFalse();

        layout.Tier = LayoutTier.Compact;

        vm.PickerPanelWidth.Should().Be(320);
        vm.TitleVisible.Should().BeFalse("紧凑档外壳顶栏已有标题");
        vm.DrawerMaskVisible.Should().BeFalse();
    }

    [Fact]
    public void 排座工作台_桌面档_内联与文字显示()
    {
        var layout = new ShellLayoutService();
        var vm = CreateSeatingVm(layout);

        vm.TitleVisible.Should().BeTrue();
        vm.CommandTextVisible.Should().BeTrue();
        vm.PickerPanelWidth.Should().Be(248);
        vm.InspectorPanelWidth.Should().Be(320);
        vm.PickerVisible.Should().BeTrue();
        vm.InspectorVisible.Should().BeTrue();
    }

    // ═══════════════ 窄屏显性选择器（会场/数据集，替代藏进图标按钮） ═══════════════

    [Fact]
    public void 排座工作台_窄屏_显性选择器随状态切换()
    {
        var layout = new ShellLayoutService();
        var vm = CreateSeatingVm(layout);

        // 桌面：不使用内联下拉（左选择栏常显）
        vm.ShowWelcomeSelectors.Should().BeFalse();
        vm.ShowGeneratedSelectorBar.Should().BeFalse();

        // 紧凑/手机空态：欢迎卡内下拉显示
        layout.Tier = LayoutTier.Compact;
        vm.ShowWelcomeSelectors.Should().BeTrue();
        vm.ShowGeneratedSelectorBar.Should().BeFalse();

        // 生成后：改为命令栏下方选择条
        vm.SeedGuideData();
        vm.ShowGeneratedSelectorBar.Should().BeTrue();
    }

    [AvaloniaFact]
    public void 排座工作台视图_手机宽度_显性选择器可见且不溢出()
    {
        var layout = new ShellLayoutService { Tier = LayoutTier.Phone };
        var vm = CreateSeatingVm(layout);
        var view = new SeatingArrangementView { DataContext = vm };
        var window = new Window
        {
            Width = 360,
            Height = 640,
            Content = view
        };
        window.Show();
        window.UpdateLayout();

        // 空态：欢迎卡内下拉可见、选择条隐藏
        var welcome = view.FindControl<StackPanel>("WelcomeSelectors");
        welcome.Should().NotBeNull();
        welcome!.IsEffectivelyVisible.Should().BeTrue("手机档空态应在欢迎卡内显示显性下拉");
        welcome.Bounds.Width.Should().BeGreaterThan(0);
        view.FindControl<Border>("GeneratedSelectorBar")!.IsEffectivelyVisible.Should().BeFalse();

        // 生成后：选择条可见且两个下拉各占约半宽
        vm.SeedGuideData();
        window.UpdateLayout();
        var bar = view.FindControl<Border>("GeneratedSelectorBar")!;
        bar.IsEffectivelyVisible.Should().BeTrue("手机档生成态应在命令栏下方显示选择条");
        bar.Bounds.Width.Should().BeLessThanOrEqualTo(360.5);
        var combos = bar.GetVisualDescendants().OfType<ComboBox>().ToList();
        combos.Should().HaveCount(2);
        combos.Should().OnlyContain(c => c.Bounds.Width > 100, "两个下拉应各占约半宽");
        welcome.IsEffectivelyVisible.Should().BeFalse("生成后欢迎卡隐藏");

        window.Close();
    }

    // ═══════════════ 引导步骤适配（引导时自动展开侧栏/面板，使目标可见） ═══════════════

    [Fact]
    public void 引导适配_桌面档_侧栏目标_强制展开侧栏()
    {
        var layout = new ShellLayoutService();
        var shell = CreateShell(layout);
        shell.ToggleSidebarCommand.Execute(null);
        shell.IsSidebarExpanded.Should().BeFalse();

        shell.PrepareGuideTarget("ToggleSidebarButton");

        shell.IsSidebarExpanded.Should().BeTrue("引导步骤目标在侧栏时应强制展开");
    }

    [Fact]
    public void 引导适配_紧凑档_侧栏目标_打开导航抽屉()
    {
        var layout = new ShellLayoutService { Tier = LayoutTier.Compact };
        var shell = CreateShell(layout);

        shell.PrepareGuideTarget("ToggleSidebarButton");

        shell.IsNavDrawerOpen.Should().BeTrue("紧凑档侧栏在抽屉内，引导应打开抽屉让目标可见");
    }

    [Fact]
    public void 引导适配_手机档_侧栏目标_不打开任何抽屉()
    {
        var layout = new ShellLayoutService { Tier = LayoutTier.Phone };
        var shell = CreateShell(layout);

        shell.PrepareGuideTarget("ToggleSidebarButton");

        shell.IsNavDrawerOpen.Should().BeFalse("手机档无侧栏，交由目标解析回退居中卡片");
    }

    [Fact]
    public void 引导适配_排座列表目标_打开选择栏_下一步页面目标_收起()
    {
        var layout = new ShellLayoutService { Tier = LayoutTier.Compact };
        var navigation = Substitute.For<INavigationService>();
        var seating = CreateSeatingVm(layout);
        navigation.CurrentViewModel.Returns(seating);
        var shell = CreateShell(layout, navigation);

        shell.PrepareGuideTarget("DatasetListBox");
        seating.IsPickerDrawerOpen.Should().BeTrue("排座数据选择目标位于左选择栏（窄屏为抽屉）");

        // 下一步目标在画布/命令栏 → 应收起已打开的面板与抽屉，避免遮挡
        shell.PrepareGuideTarget("GenerateButton");
        seating.IsPickerDrawerOpen.Should().BeFalse();
        shell.IsNavDrawerOpen.Should().BeFalse();
    }

    [Fact]
    public void 引导适配_策略列表目标_打开列表面板()
    {
        var layout = new ShellLayoutService { Tier = LayoutTier.Phone };
        var navigation = Substitute.For<INavigationService>();
        var strategy = new StrategyConfigurationViewModel(
            Substitute.For<IApplicationFacade>(), layout, NullDialogService.Instance);
        navigation.CurrentViewModel.Returns(strategy);
        var shell = CreateShell(layout, navigation);

        shell.PrepareGuideTarget("StrategyListBox");

        strategy.ListPanel.IsOpen.Should().BeTrue("策略列表目标位于列表面板，手机档应打开全屏列表");
    }

    // ═══════════════ 外壳视图（Headless）三档可见性 ═══════════════

    [AvaloniaFact]
    public void 外壳视图_三档_Tab栏与顶栏跟随档位_且不溢出视口()
    {
        static (MainShellViewModel Vm, MainView View, Window Window, ShellLayoutService Layout) CreateAt(double width)
        {
            var layout = new ShellLayoutService();
            var vm = CreateShell(layout);
            var view = new MainView(Substitute.For<IOnboardingService>()) { DataContext = vm };
            var window = new Window { Width = width, Height = 800, Content = view };
            window.Show();
            window.UpdateLayout();
            return (vm, view, window, layout);
        }

        static Border TabBar(MainView v) => v.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("sf-tabbar"));

        var desktop = CreateAt(1200);
        desktop.Layout.Tier.Should().Be(LayoutTier.Desktop);
        TabBar(desktop.View).IsVisible.Should().BeFalse("桌面档无底部 Tab 栏");
        desktop.Window.Close();

        var compact = CreateAt(750);
        compact.Layout.Tier.Should().Be(LayoutTier.Compact);
        TabBar(compact.View).IsVisible.Should().BeFalse("紧凑档由外壳顶栏导航");
        compact.Window.Close();

        var phone = CreateAt(390);
        phone.Layout.Tier.Should().Be(LayoutTier.Phone);
        TabBar(phone.View).IsVisible.Should().BeTrue("手机档显示底部 Tab 栏");
        TabBar(phone.View).Bounds.Width.Should().BeLessThanOrEqualTo(390.5);

        var tabButtons = TabBar(phone.View).GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("sf-tab"))
            .Where(b => b.IsVisible)
            .ToList();
        tabButtons.Should().NotBeEmpty();
        tabButtons.Should().OnlyContain(b => b.Bounds.Width > 0, "每个可见 Tab 应有布局宽度");
        phone.Window.Close();
    }

    // ═══════════════ 排座工作台（Headless）手机宽度不溢出 ═══════════════

    [AvaloniaFact]
    public void 排座工作台视图_手机宽度_命令栏与全屏面板不溢出()
    {
        var layout = new ShellLayoutService { Tier = LayoutTier.Phone };
        var vm = CreateSeatingVm(layout);
        vm.SeedGuideData();
        var view = new SeatingArrangementView { DataContext = vm };
        var window = new Window
        {
            Width = 360,
            Height = 640,
            Content = view
        };
        window.Show();
        window.UpdateLayout();

        layout.Tier.Should().Be(LayoutTier.Phone);
        view.Bounds.Width.Should().Be(360, "视图应贴合手机宽度视口");
        view.FindControl<Button>("ExportButton")!.IsVisible.Should().BeFalse("手机档导出按钮收进 ⋯ 菜单");

        // 生成按钮（命令栏最右主操作）应位于工具栏内
        var generate = view.FindControl<Button>("GenerateButton");
        generate.Should().NotBeNull();
        var toolbar = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("sf-toolbar"));
        toolbar.Bounds.Width.Should().BeLessThanOrEqualTo(360.5);

        // 命令栏按钮区：每个可见按钮（含 ⋯）不超出视口右缘
        var buttonStrip = (Visual)generate!.Parent!;
        var stripOrigin = buttonStrip.TranslatePoint(default(Point), view);
        stripOrigin.Should().NotBeNull();
        var stripButtons = buttonStrip.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsVisible && b.Bounds.Width > 0)
            .ToList();
        stripButtons.Should().NotBeEmpty();
        foreach (var button in stripButtons)
        {
            (stripOrigin!.Value.X + button.Bounds.Right).Should().BeLessThanOrEqualTo(361,
                $"命令栏按钮不应溢出 360px 视口（按钮 x={button.Bounds.X} w={button.Bounds.Width}）");
        }

        // 检查器全屏面板宽度应贴合视口（NaN 全宽拉伸）
        var inspector = view.FindControl<Border>("InspectorPanel");
        inspector.Should().NotBeNull();
        vm.ToggleInspectorDrawerCommand.Execute(null);
        window.UpdateLayout();
        inspector!.IsVisible.Should().BeTrue();
        inspector.Bounds.Width.Should().BeLessThanOrEqualTo(360.5);
        inspector.Bounds.Width.Should().BeGreaterThan(300, "手机档检查器为全屏覆盖");

        window.Close();
    }
}
