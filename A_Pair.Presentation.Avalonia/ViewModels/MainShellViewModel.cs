using System;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Presentation.Avalonia.Services;
using Avalonia.Animation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class MainShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IApplicationFacade _facade;

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
    private IPageTransition? _pageTransition;

    [ObservableProperty]
    private PageTransitionType _currentTransitionType;

    [ObservableProperty]
    private bool _isPageLoading;

    private bool _userWantsExpanded = true;
    private CancellationTokenSource? _pageLoadCts;

    /// <summary>loading 遮罩最短显示时间，确保动画完成且不闪。</summary>
    private static readonly TimeSpan MinLoadDuration = TimeSpan.FromMilliseconds(350);

    public MainShellViewModel(INavigationService navigation, IApplicationFacade facade)
    {
        _navigation = navigation;
        _facade = facade;
        // 必须在首次设置 CurrentViewModel 之前同步赋默认值，
        // 确保 TransitioningContentControl 从第一页就正确初始化过渡管道。
        PageTransition = CreateTransition(PageTransitionType.CrossFade);
        _navigation.CurrentViewModelChanged += () =>
        {
            IsPageLoading = true;
            CurrentViewModel = _navigation.CurrentViewModel;
            CurrentPage = _navigation.CurrentPage;
            _ = DelayCloseLoadingAsync();
        };
        CurrentViewModel = _navigation.CurrentViewModel;
        CurrentPage = _navigation.CurrentPage;
        _ = DelayCloseLoadingAsync();
        _ = LoadTransitionSettingAsync(CancellationToken.None);
    }

    /// <summary>
    /// 等待最短显示时间后关闭 loading 遮罩。
    /// 用 Task.Delay 替代 Dispatcher 确保 PageSlide 动画（250ms）在遮罩背后完整播完。
    /// </summary>
    private async Task DelayCloseLoadingAsync()
    {
        _pageLoadCts?.Cancel();
        _pageLoadCts = new CancellationTokenSource();
        var ct = _pageLoadCts.Token;

        try
        {
            await Task.Delay(MinLoadDuration, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        IsPageLoading = false;
    }

    private async Task LoadTransitionSettingAsync(CancellationToken ct)
    {
        try
        {
            var settings = await _facade.LoadAppSettingsAsync(ct);
            ApplyTransitionType(settings.TransitionAnimation);
        }
        catch
        {
            ApplyTransitionType(PageTransitionType.CrossFade);
        }
    }

    public void ApplyTransitionType(PageTransitionType type)
    {
        CurrentTransitionType = type;
        PageTransition = CreateTransition(type);
    }

    private static IPageTransition? CreateTransition(PageTransitionType type)
    {
        return type switch
        {
            PageTransitionType.CrossFade => new CrossFade(TimeSpan.FromMilliseconds(250)),
            PageTransitionType.SlideHorizontal => new PageSlide(TimeSpan.FromMilliseconds(250), PageSlide.SlideAxis.Horizontal),
            PageTransitionType.SlideVertical => new PageSlide(TimeSpan.FromMilliseconds(250), PageSlide.SlideAxis.Vertical),
            PageTransitionType.Composite => new CompositePageTransition
            {
                PageTransitions =
                {
                    new CrossFade(TimeSpan.FromMilliseconds(250)),
                    new PageSlide(TimeSpan.FromMilliseconds(250), PageSlide.SlideAxis.Horizontal)
                }
            },
            PageTransitionType.None => null,
            _ => new CrossFade(TimeSpan.FromMilliseconds(250))
        };
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
