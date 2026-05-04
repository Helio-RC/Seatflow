using System;
using A_Pair.Presentation.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace A_Pair.Presentation.Avalonia.Services;

public class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;

    public ViewModelBase CurrentViewModel { get; private set; } = default!;
    public PageKey CurrentPage { get; private set; } = (PageKey)(-1);
    public event Action? CurrentViewModelChanged;

    public NavigationService (IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        NavigateTo(PageKey.Home);
    }

    public void NavigateTo (PageKey page)
    {
        if (page == CurrentPage) return;
        CurrentPage = page;
        CurrentViewModel = page switch
        {
            PageKey.Home => _serviceProvider.GetRequiredService<HomeViewModel>(),
            PageKey.DataManagement => _serviceProvider.GetRequiredService<DataManagementViewModel>(),
            PageKey.VenueConfiguration => _serviceProvider.GetRequiredService<VenueConfigurationViewModel>(),
            PageKey.FreeformManagement => _serviceProvider.GetRequiredService<FreeformManagementViewModel>(),
            PageKey.StrategyConfiguration => _serviceProvider.GetRequiredService<StrategyConfigurationViewModel>(),
            PageKey.SeatingArrangement => _serviceProvider.GetRequiredService<SeatingArrangementViewModel>(),
            PageKey.SnapshotHistory => _serviceProvider.GetRequiredService<SnapshotHistoryViewModel>(),
            PageKey.PluginManagement => _serviceProvider.GetRequiredService<PluginManagementViewModel>(),
            PageKey.Settings => _serviceProvider.GetRequiredService<SettingsViewModel>(),
            PageKey.About => _serviceProvider.GetRequiredService<AboutViewModel>(),
            _ => throw new ArgumentOutOfRangeException(nameof(page))
        };
        CurrentViewModelChanged?.Invoke();
    }
}
