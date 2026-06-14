using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Presentation.Avalonia.Lang;
using A_Pair.Presentation.Avalonia.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class MainShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly ILogger<MainShellViewModel> _logger;

    [ObservableProperty]
    public partial ViewModelBase CurrentViewModel { get; set; } = default!;

    [ObservableProperty]
    public partial PageKey CurrentPage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarCollapsed))]
    public partial bool IsSidebarExpanded { get; set; } = true;

    public bool IsSidebarCollapsed => !IsSidebarExpanded;

    [ObservableProperty]
    public partial double SidebarWidth { get; set; } = 140;

    [ObservableProperty]
    public partial bool IsPageLoading { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingContentVisible { get; set; }

    [ObservableProperty]
    public partial double PageOpacity { get; set; } = 1.0;

    /// <summary>是否处于首次启动引导模式。</summary>
    [ObservableProperty]
    public partial bool IsOnboardingActive { get; set; }

    private readonly Dictionary<string, bool> _pageNav = [];
    private bool _userWantsExpanded = true;
    private CancellationTokenSource? _pageLoadCts;

    /// <summary>加载页面导航配置（page_navigation.json 嵌入资源）。</summary>
    private static Dictionary<string, bool> LoadPageNav ()
    {
        try
        {
            var assembly = typeof(MainShellViewModel).Assembly;
            const string resourceName = "A_Pair.Presentation.Avalonia.Data.page_navigation.json";
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
            // 静默回退：全部页面保持启用
            return [];
        }
    }

    private bool IsPageEnabled (string key) =>
        !_pageNav.TryGetValue(key , out var enabled) || enabled;

    public double PluginManagementOpacity => IsPageEnabled("PluginManagement") ? 1.0 : 0.4;
    public string? PluginManagementDisabledTip =>
        IsPageEnabled("PluginManagement") ? null : Resources.Nav_PluginDisabled;

    /// <summary>页面淡出时长（对应 AXAML ContentControl Opacity 0.35s）。</summary>
    private static readonly TimeSpan StaggerDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(350);

    /// <summary>遮罩最短显示时间。</summary>
    private static readonly TimeSpan MinLoadDuration = TimeSpan.FromMilliseconds(500);

    /// <summary>内容切换后延迟显示进度条。</summary>
    private static readonly TimeSpan ProgressBarDelay = TimeSpan.FromMilliseconds(120);

    public MainShellViewModel (INavigationService navigation , ILogger<MainShellViewModel>? logger = null)
    {
        _navigation = navigation;
        _logger = logger ?? NullLogger<MainShellViewModel>.Instance;
        _pageNav = LoadPageNav();
        _navigation.CurrentViewModelChanged += () => _ = RunTransitionAsync();
        CurrentViewModel = _navigation.CurrentViewModel;
        CurrentPage = _navigation.CurrentPage;
    }

    /// <summary>
    /// ===== 阶段1：旧页淡出 ‖ 遮罩淡入（并行） =====
    /// ===== 阶段2：内容切换（遮罩背后）           =====
    /// ===== 阶段3：进度条渐显 + 等待加载          =====
    /// ===== 阶段4：进度条渐隐                     =====
    /// ===== 阶段5：新页淡入 ‖ 遮罩淡出（并行）   =====
    /// </summary>
    private async Task RunTransitionAsync ()
    {
        _pageLoadCts?.Cancel();
        _pageLoadCts = new CancellationTokenSource();
        var ct = _pageLoadCts.Token;
        IsLoadingContentVisible = false;

        var newVm = _navigation.CurrentViewModel;
        var newPage = _navigation.CurrentPage;

        // 引导模式下跳过动画，直接切换
        if (IsOnboardingActive)
        {
            CurrentViewModel = newVm;
            CurrentPage = newPage;
            return;
        }

        try
        {
            // 阶段1：遮罩先淡入 → 旧页随后淡出（错开）
            IsPageLoading = true;
            await Task.Delay(StaggerDelay , ct);
            PageOpacity = 0;
            await Task.Delay(FadeOutDuration , ct);

            // 阶段2：内容切换（遮罩背后，不可见）
            CurrentViewModel = newVm;
            CurrentPage = newPage;

            // 阶段3：进度条渐显 + 等待加载
            await Task.Delay(ProgressBarDelay , ct);
            IsLoadingContentVisible = true;

            var layoutDone = new TaskCompletionSource();
            Dispatcher.UIThread.Post(() => layoutDone.TrySetResult() , DispatcherPriority.Loaded);
            await Task.WhenAll(Task.Delay(MinLoadDuration , ct) , layoutDone.Task);

            // 阶段4：进度条渐隐
            IsLoadingContentVisible = false;

            // 阶段5：新页先淡入 → 遮罩随后淡出（错开）
            PageOpacity = 1;
            await Task.Delay(StaggerDelay , ct);
        }
        catch (OperationCanceledException)
        {
            IsLoadingContentVisible = false;
            PageOpacity = 1;
            IsPageLoading = false;
            return;
        }

        IsPageLoading = false;
    }

    public void OnWindowWidthChanged (double windowWidth)
    {
        // 引导期间不自动折叠侧边栏
        if (IsOnboardingActive)
            return;

        if (windowWidth < 750)
            IsSidebarExpanded = false;
        else
            IsSidebarExpanded = _userWantsExpanded;
    }

    partial void OnIsSidebarExpandedChanged (bool value)
        => SidebarWidth = value ? 140 : 64;

    [RelayCommand]
    private void ToggleSidebar ()
    {
        _userWantsExpanded = !_userWantsExpanded;
        IsSidebarExpanded = _userWantsExpanded;
    }

    [RelayCommand]
    private async Task NavigateAsync (string pageName)
    {
        if (Enum.TryParse<PageKey>(pageName , out var key))
        {
            if (!IsPageEnabled(pageName))
                return;
            await _navigation.NavigateToAsync(key);
        }
    }

    /// <summary>强制展开侧边栏（引导期间使用）。</summary>
    public void EnsureSidebarExpanded ()
    {
        _userWantsExpanded = true;
        IsSidebarExpanded = true;
    }

    /// <summary>引导模式下的页面导航（同步，无动画）。</summary>
    public void OnboardingNavigateTo (PageKey page)
    {
        _navigation.NavigateTo(page);
    }

    /// <summary>完成引导：关闭引导模式，持久化标记，回到首页。</summary>
    public async Task CompleteOnboardingAsync ()
    {
        IsOnboardingActive = false;

        try
        {
            // 通过 App 的 ServiceProvider 获取 Facade
            if (global::Avalonia.Application.Current is App app)
            {
                var facade = app.ServiceProvider.GetRequiredService<IApplicationFacade>();
                var settings = await facade.LoadAppSettingsAsync();
                if (settings.IsFirstLaunch)
                {
                    settings.IsFirstLaunch = false;
                    await facade.SaveAppSettingsAsync(settings);
                }
            }
        }
        catch
        {
            // 保存失败不影响用户体验
        }

        // 回到首页
        _navigation.NavigateTo(PageKey.Home);
    }
}
