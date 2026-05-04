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

    private bool _userWantsExpanded = true;
    private CancellationTokenSource? _pageLoadCts;

    /// <summary>loading 遮罩最短显示时间。</summary>
    private static readonly TimeSpan MinLoadDuration = TimeSpan.FromMilliseconds(350);

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
    /// 等待页面布局完成 AND 最短显示时间后关闭遮罩。
    /// DispatcherPriority.Loaded 在绑定+布局完成后执行，确保新页面已渲染。
    /// </summary>
    private async Task DelayCloseLoadingAsync()
    {
        _pageLoadCts?.Cancel();
        _pageLoadCts = new CancellationTokenSource();
        var ct = _pageLoadCts.Token;

        // 用 Dispatcher.Loaded 等待页面布局完成
        var layoutDone = new TaskCompletionSource();
        Dispatcher.UIThread.Post(() => layoutDone.TrySetResult(), DispatcherPriority.Loaded);

        try
        {
            await Task.WhenAll(Task.Delay(MinLoadDuration, ct), layoutDone.Task);
        }
        catch (OperationCanceledException)
        {
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
