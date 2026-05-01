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

    [RelayCommand]
    private void Navigate(string pageName)
    {
        if (Enum.TryParse<PageKey>(pageName, out var key))
            _navigation.NavigateTo(key);
    }
}
