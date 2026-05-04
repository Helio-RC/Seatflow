using System;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Presentation.Avalonia.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class MainShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;

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

    private bool _userWantsExpanded = true;
    private CancellationTokenSource? _pageLoadCts;

    /// <summary>遮罩最短显示时间。</summary>
    private static readonly TimeSpan MinLoadDuration = TimeSpan.FromMilliseconds(350);

    /// <summary>遮罩背景淡入后，延迟此时间再显示加载条。</summary>
    private static readonly TimeSpan ContentFadeInDelay = TimeSpan.FromMilliseconds(180);

    public MainShellViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        _navigation.CurrentViewModelChanged += () =>
        {
            IsPageLoading = true;
            CurrentViewModel = _navigation.CurrentViewModel;
            CurrentPage = _navigation.CurrentPage;
            _ = DelayCloseLoadingAsync();
        };
        CurrentViewModel = _navigation.CurrentViewModel;
        CurrentPage = _navigation.CurrentPage;
    }

    /// <summary>
    /// 动画序列：遮罩背景先淡入 → 加载条随后淡入 → 等页面布局+最短时间 → 整体淡出。
    /// </summary>
    private async Task DelayCloseLoadingAsync()
    {
        _pageLoadCts?.Cancel();
        _pageLoadCts = new CancellationTokenSource();
        var ct = _pageLoadCts.Token;
        IsLoadingContentVisible = false;

        try
        {
            // 阶段1: 遮罩背景已开始淡入，延迟后显示加载条
            await Task.Delay(ContentFadeInDelay, ct);
            IsLoadingContentVisible = true;

            // 阶段2: 等待布局完成 + 最短显示时间
            var layoutDone = new TaskCompletionSource();
            Dispatcher.UIThread.Post(() => layoutDone.TrySetResult(), DispatcherPriority.Loaded);
            await Task.WhenAll(Task.Delay(MinLoadDuration, ct), layoutDone.Task);

            // 阶段3: 整体淡出（遮罩背景 + 加载条同时消失）
            IsLoadingContentVisible = false;
        }
        catch (OperationCanceledException)
        {
            IsLoadingContentVisible = false;
            return;
        }

        IsPageLoading = false;
    }

    public void OnWindowWidthChanged(double windowWidth)
    {
        if (windowWidth < 750)
            IsSidebarExpanded = false;
        else
            IsSidebarExpanded = _userWantsExpanded;
    }

    partial void OnIsSidebarExpandedChanged(bool value)
        => SidebarWidth = value ? 140 : 64;

    [RelayCommand]
    private void ToggleSidebar()
    {
        _userWantsExpanded = !_userWantsExpanded;
        IsSidebarExpanded = _userWantsExpanded;
    }

    [RelayCommand]
    private void Navigate(string pageName)
    {
        if (Enum.TryParse<PageKey>(pageName, out var key))
            _navigation.NavigateTo(key);
    }
}
