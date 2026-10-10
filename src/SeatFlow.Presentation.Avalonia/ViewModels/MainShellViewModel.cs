using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using SeatFlow.Application.Interfaces;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 外壳 ViewModel（M3 新 IA，M7 响应式三档）：
/// - 侧栏分组（工作流/资料/规则/记录 + 底部设置/关于），中部导航可滚动（修复 I-01）；
/// - 页面切换即时化（删除 200ms 淡出 + 100ms 间隔；浏览器端原本就跳过）；
/// - 紧凑档（600–900px）：侧栏收起为左抽屉（遮罩关闭），顶栏显示导航入口；
/// - 手机档（≤600px）：底部 Tab 导航（排座/人员/会场/策略/更多），无外壳顶栏与侧栏；
/// - Home 页移除后默认入口为排座工作台。
/// </summary>
public partial class MainShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IApplicationFacade _facade;
    private readonly IOnboardingService _onboarding;
    private readonly IShellLayoutService _layout;
    private readonly ILogger<MainShellViewModel> _logger;

    [ObservableProperty]
    public partial ViewModelBase CurrentViewModel { get; set; } = default!;

    [ObservableProperty]
    public partial PageKey CurrentPage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarCollapsed))]
    [NotifyPropertyChangedFor(nameof(RailDisplayWidth))]
    public partial bool IsSidebarExpanded { get; set; } = true;

    public bool IsSidebarCollapsed => !IsSidebarExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RailDisplayWidth))]
    public partial double SidebarWidth { get; set; } = 172;

    /// <summary>是否处于首次启动引导模式。</summary>
    [ObservableProperty]
    public partial bool IsOnboardingActive { get; set; }

    /// <summary>紧凑档下导航左抽屉是否展开（手机档无侧栏）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNavDrawerVisible))]
    [NotifyPropertyChangedFor(nameof(IsMaskVisible))]
    public partial bool IsNavDrawerOpen { get; set; }

    /// <summary>外壳布局状态（页面读取布局档位用）。</summary>
    public IShellLayoutService Layout => _layout;

    /// <summary>侧栏是否可见：桌面常驻；仅紧凑档在抽屉展开时显示（手机档由底部 Tab 代替）。</summary>
    public bool IsNavDrawerVisible =>
        _layout.Tier == LayoutTier.Desktop
        || (_layout.Tier == LayoutTier.Compact && IsNavDrawerOpen);

    /// <summary>遮罩是否可见（仅紧凑档 + 抽屉展开）。</summary>
    public bool IsMaskVisible => _layout.Tier == LayoutTier.Compact && IsNavDrawerOpen;

    /// <summary>侧栏实际宽度：紧凑抽屉固定 280，桌面沿用折叠/展开宽度。</summary>
    public double RailDisplayWidth => _layout.Tier == LayoutTier.Compact ? 280 : SidebarWidth;

    /// <summary>紧凑档外壳顶栏是否可见（手机档无外壳顶栏，页面命令栏自带标题）。</summary>
    public bool IsCompactTopBarVisible => _layout.Tier == LayoutTier.Compact;

    /// <summary>手机档底部 Tab 栏是否可见（档位为手机档且至少一个 Tab/「更多」启用，避免空白栏）。</summary>
    public bool IsPhoneTabBarVisible =>
        _layout.Tier == LayoutTier.Phone
        && (IsSeatingNavEnabled || IsMembersNavEnabled || IsVenuesNavEnabled
            || IsStrategiesNavEnabled || IsMoreTabVisible);

    /// <summary>是否显示导航文字标签（折叠态隐藏）。</summary>
    public bool NavLabelVisible => IsSidebarExpanded;

    /// <summary>导航按钮内容对齐（展开左对齐 / 折叠居中）。</summary>
    public global::Avalonia.Layout.HorizontalAlignment NavContentAlignment =>
        IsSidebarExpanded ? global::Avalonia.Layout.HorizontalAlignment.Left : global::Avalonia.Layout.HorizontalAlignment.Center;

    /// <summary>导航按钮内边距（展开 8,0,0,0 / 折叠 0）。</summary>
    public global::Avalonia.Thickness NavButtonPadding =>
        IsSidebarExpanded ? new global::Avalonia.Thickness(8, 0, 0, 0) : new global::Avalonia.Thickness(0);

    /// <summary>侧栏所在列（桌面 0；紧凑切到内容列做抽屉覆盖）。</summary>
    public int RailGridColumn => _layout.Tier == LayoutTier.Compact ? 1 : 0;

    /// <summary>侧栏水平对齐（紧凑抽屉靠左覆盖）。</summary>
    public global::Avalonia.Layout.HorizontalAlignment RailHorizontalAlignment =>
        _layout.Tier == LayoutTier.Compact ? global::Avalonia.Layout.HorizontalAlignment.Left : global::Avalonia.Layout.HorizontalAlignment.Stretch;

    /// <summary>侧栏层级（紧凑抽屉需在遮罩之上）。</summary>
    public int RailZIndex => _layout.Tier == LayoutTier.Compact ? 40 : 0;

    /// <summary>侧栏描边（紧凑抽屉右边线）。</summary>
    public global::Avalonia.Thickness RailBorderThickness =>
        _layout.Tier == LayoutTier.Compact ? new global::Avalonia.Thickness(0, 0, 1, 0) : new global::Avalonia.Thickness(0);

    // ── 导航激活态（供 sf-active 类绑定） ──
    public bool IsSeatingPage => CurrentPage == PageKey.SeatingArrangement;
    public bool IsMembersPage => CurrentPage == PageKey.MemberManagement;
    public bool IsVenuesPage => CurrentPage == PageKey.VenueConfiguration;
    public bool IsStrategiesPage => CurrentPage == PageKey.StrategyConfiguration;
    public bool IsSnapshotsPage => CurrentPage == PageKey.SnapshotHistory;
    public bool IsSettingsPage => CurrentPage == PageKey.Settings;
    public bool IsAboutPage => CurrentPage == PageKey.About;

    // ── 页面开关（page_navigation.json；手机档 Tab / 「更多」菜单据此过滤） ──
    public bool IsSeatingNavEnabled => IsPageEnabled(nameof(PageKey.SeatingArrangement));
    public bool IsMembersNavEnabled => IsPageEnabled(nameof(PageKey.MemberManagement));
    public bool IsVenuesNavEnabled => IsPageEnabled(nameof(PageKey.VenueConfiguration));
    public bool IsStrategiesNavEnabled => IsPageEnabled(nameof(PageKey.StrategyConfiguration));
    public bool IsSnapshotsNavEnabled => IsPageEnabled(nameof(PageKey.SnapshotHistory));
    public bool IsSettingsNavEnabled => IsPageEnabled(nameof(PageKey.Settings));
    public bool IsAboutNavEnabled => IsPageEnabled(nameof(PageKey.About));

    /// <summary>「更多」Tab 是否可见（历史快照/设置/关于至少一个启用）。</summary>
    public bool IsMoreTabVisible => IsSnapshotsNavEnabled || IsSettingsNavEnabled || IsAboutNavEnabled;

    /// <summary>「更多」Tab 激活态（当前页位于其菜单内）。</summary>
    public bool IsMoreActive => IsSnapshotsPage || IsSettingsPage || IsAboutPage;

    /// <summary>紧凑顶栏标题。</summary>
    public string CurrentPageTitle => CurrentPage switch
    {
        PageKey.SeatingArrangement => Resources.Nav_Seating,
        PageKey.MemberManagement => Resources.Nav_MemberManagement,
        PageKey.VenueConfiguration => Resources.Nav_VenueConfig,
        PageKey.StrategyConfiguration => Resources.Nav_StrategyConfig,
        PageKey.SnapshotHistory => Resources.Nav_SnapshotHistory,
        PageKey.Settings => Resources.Nav_Settings,
        PageKey.About => Resources.Nav_About,
        _ => Resources.App_Title
    };

    private readonly Dictionary<string, bool> _pageNav = [];
    private bool _userWantsExpanded = true;

    /// <summary>加载页面导航配置（page_navigation.json 嵌入资源）。</summary>
    private Dictionary<string, bool> LoadPageNav()
    {
        try
        {
            var assembly = typeof(MainShellViewModel).Assembly;
            const string resourceName = "SeatFlow.Presentation.Avalonia.Data.page_navigation.json";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return [];
            using var doc = JsonDocument.Parse(stream);
            var pages = doc.RootElement.GetProperty("pages");
            var result = new Dictionary<string, bool>();
            foreach (var p in pages.EnumerateObject())
                result[p.Name] = p.Value.GetBoolean();
            return result;
        }
        catch
        {
            _logger?.LogWarning("无法加载 page_navigation.json 嵌入资源");
            return [];
        }
    }

    private bool IsPageEnabled(string key) =>
        !_pageNav.TryGetValue(key, out var enabled) || enabled;

    public MainShellViewModel(INavigationService navigation, IApplicationFacade facade, IOnboardingService onboarding, IShellLayoutService layout, IDialogService dialog, ILogger<MainShellViewModel>? logger = null) : base(dialog, logger)
    {
        _navigation = navigation;
        _facade = facade;
        _onboarding = onboarding;
        _layout = layout;
        _logger = logger ?? NullLogger<MainShellViewModel>.Instance;
        _pageNav = LoadPageNav();
        _layout.PropertyChanged += OnLayoutChanged;
        _navigation.CurrentViewModelChanged += OnCurrentViewModelChanged;
        CurrentViewModel = _navigation.CurrentViewModel;
        CurrentPage = _navigation.CurrentPage;
        // 不在此触发页面引导：初始页无对应 pageGuide；页面引导的触发统一在切页后处理。
    }

    private void OnLayoutChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IShellLayoutService.Tier)) return;
        if (_layout.Tier != LayoutTier.Compact) IsNavDrawerOpen = false;
        OnPropertyChanged(nameof(IsNavDrawerVisible));
        OnPropertyChanged(nameof(IsMaskVisible));
        OnPropertyChanged(nameof(RailDisplayWidth));
        OnPropertyChanged(nameof(RailGridColumn));
        OnPropertyChanged(nameof(RailHorizontalAlignment));
        OnPropertyChanged(nameof(RailZIndex));
        OnPropertyChanged(nameof(RailBorderThickness));
        OnPropertyChanged(nameof(IsCompactTopBarVisible));
        OnPropertyChanged(nameof(IsPhoneTabBarVisible));
    }

    /// <summary>切页即时化：同步替换内容（无强制淡出/间隔），随后检查页面引导。</summary>
    private void OnCurrentViewModelChanged()
    {
        CurrentViewModel = _navigation.CurrentViewModel;
        CurrentPage = _navigation.CurrentPage;
        IsNavDrawerOpen = false;
        SchedulePageGuideCheck();
    }

    /// <summary>延迟触发页面引导检查（等页面渲染完成）。</summary>
    private void SchedulePageGuideCheck()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _onboarding.TryShowPageGuide(CurrentPage);
        }, DispatcherPriority.Background);
    }

    public void OnWindowWidthChanged(double windowWidth)
    {
        // 初始布局 Bounds 为 0：忽略，等待首次真实尺寸（避免误判为手机档）
        if (windowWidth <= 0) return;

        _layout.Tier = LayoutBreakpoints.Resolve(windowWidth);

        // 引导期间不自动折叠侧边栏
        if (IsOnboardingActive)
            return;

        if (_layout.IsNarrow)
        {
            IsNavDrawerOpen = false;
            // 紧凑抽屉始终显示完整导航文字（280px），不受桌面折叠偏好影响
            IsSidebarExpanded = true;
        }
        else
        {
            IsSidebarExpanded = _userWantsExpanded;
        }
    }

    partial void OnIsSidebarExpandedChanged(bool value)
    {
        SidebarWidth = value ? 172 : 64;
        OnPropertyChanged(nameof(NavLabelVisible));
        OnPropertyChanged(nameof(NavContentAlignment));
        OnPropertyChanged(nameof(NavButtonPadding));
    }

    partial void OnCurrentPageChanged(PageKey value)
    {
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(IsSeatingPage));
        OnPropertyChanged(nameof(IsMembersPage));
        OnPropertyChanged(nameof(IsVenuesPage));
        OnPropertyChanged(nameof(IsStrategiesPage));
        OnPropertyChanged(nameof(IsSnapshotsPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(IsAboutPage));
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        _userWantsExpanded = !_userWantsExpanded;
        IsSidebarExpanded = _userWantsExpanded;
    }

    [RelayCommand]
    private void ToggleNavDrawer()
    {
        IsNavDrawerOpen = !IsNavDrawerOpen;
    }

    [RelayCommand]
    private void CloseNavDrawer()
    {
        IsNavDrawerOpen = false;
    }

    [RelayCommand]
    private async Task NavigateAsync(string pageName)
    {
        if (Enum.TryParse<PageKey>(pageName, out var key))
        {
            if (!IsPageEnabled(pageName))
                return;
            IsNavDrawerOpen = false;
            await _navigation.NavigateToAsync(key);
        }
    }

    /// <summary>强制展开侧边栏（引导期间使用）。</summary>
    public void EnsureSidebarExpanded()
    {
        _userWantsExpanded = true;
        IsSidebarExpanded = true;
    }

    /// <summary>引导模式下的页面导航（同步，无动画）。</summary>
    /// <remarks>
    /// NavigateTo 触发 CurrentViewModelChanged → OnCurrentViewModelChanged 同步设置内容，
    /// 确保目标解析时 NameScope 已可用。
    /// </remarks>
    public void OnboardingNavigateTo(PageKey page)
    {
        IsNavDrawerOpen = false;
        _navigation.NavigateTo(page);
        CurrentViewModel = _navigation.CurrentViewModel;
        CurrentPage = page;
    }

    /// <summary>完成引导：关闭引导模式，持久化标记，导航到指定页面。</summary>
    public async Task CompleteOnboardingAsync(PageKey navigateTo = PageKey.SeatingArrangement)
    {
        IsOnboardingActive = false;

        try
        {
            var settings = await _facade.LoadAppSettingsAsync();
            if (settings.IsFirstLaunch)
            {
                settings.IsFirstLaunch = false;
                await _facade.SaveAppSettingsAsync(settings);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "无法持久化引导完成标记");
        }

        _navigation.NavigateTo(navigateTo);
    }
}
