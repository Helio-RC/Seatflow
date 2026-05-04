using System;
using A_Pair.Presentation.Avalonia.ViewModels;

namespace A_Pair.Presentation.Avalonia.Services;

public enum PageKey
{
    Home,
    DataManagement,
    VenueConfiguration,
    FreeformManagement,
    StrategyConfiguration,
    SeatingArrangement,
    SnapshotHistory,
    PluginManagement,
    Settings,
    About
}

public interface INavigationService
{
    ViewModelBase CurrentViewModel { get; }
    PageKey CurrentPage { get; }
    event Action? CurrentViewModelChanged;
    void NavigateTo (PageKey page);
}
