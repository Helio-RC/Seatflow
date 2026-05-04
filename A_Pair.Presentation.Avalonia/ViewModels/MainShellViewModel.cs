using System;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Presentation.Avalonia.Services;
using Avalonia.Animation;
using Avalonia.Threading;
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
            // DispatcherPriority.Loaded 在布局和绑定完成后执行，此时新页面已渲染完毕
            Dispatcher.UIThread.Post(SignalPageLoaded, DispatcherPriority.Loaded);
        };
        CurrentViewModel = _navigation.CurrentViewModel;
        CurrentPage = _navigation.CurrentPage;
        Dispatcher.UIThread.Post(SignalPageLoaded, DispatcherPriority.Loaded);
        _ = LoadTransitionSettingAsync(CancellationToken.None);
    }

    /// <summary>由 MainWindow 在新页面 View 完成布局渲染后调用。</summary>
    public void SignalPageLoaded()
    {
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
