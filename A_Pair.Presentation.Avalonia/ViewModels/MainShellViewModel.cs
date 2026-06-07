using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Presentation.Avalonia.Lang;
using A_Pair.Presentation.Avalonia.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class MainShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly ILogger<MainShellViewModel> _logger;

    [ObservableProperty]
    private ViewModelBase _currentViewModel = default!;

    [ObservableProperty]
    private PageKey _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarCollapsed))]
    private bool _isSidebarExpanded = true;

    public bool IsSidebarCollapsed => !IsSidebarExpanded;

    [ObservableProperty]
    private double _sidebarWidth = 140;

    [ObservableProperty]
    private bool _isPageLoading;

    [ObservableProperty]
    private bool _isLoadingContentVisible;

    [ObservableProperty]
    private double _pageOpacity = 1.0;

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
}
