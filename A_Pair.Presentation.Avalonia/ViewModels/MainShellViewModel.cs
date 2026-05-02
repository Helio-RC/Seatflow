using System;
using A_Pair.Presentation.Avalonia.Services;
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
    private bool _isSidebarExpanded = true;

    [ObservableProperty]
    private double _sidebarWidth = 160;

    private bool _userWantsExpanded = true;

    public MainShellViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        _navigation.CurrentViewModelChanged += () =>
        {
            CurrentViewModel = _navigation.CurrentViewModel;
            CurrentPage = _navigation.CurrentPage;
        };
        CurrentViewModel = _navigation.CurrentViewModel;
        CurrentPage = _navigation.CurrentPage;
    }

    public void OnWindowWidthChanged(double windowWidth)
    {
        if (windowWidth < 800)
            IsSidebarExpanded = false;
        else
            IsSidebarExpanded = _userWantsExpanded;
    }

    partial void OnIsSidebarExpandedChanged(bool value)
        => SidebarWidth = value ? 160 : 64;

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
