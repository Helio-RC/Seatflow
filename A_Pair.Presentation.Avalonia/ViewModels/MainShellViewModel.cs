using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace A_Pair.Presentation.Avalonia.ViewModels
{
    public class MainShellViewModel : ViewModelBase
    {
        public object CurrentView { get; private set; }

        public IRelayCommand NavigateDataManagementCommand { get; }
        public IRelayCommand NavigateVenueConfigCommand { get; }
        public IRelayCommand NavigateStrategyConfigCommand { get; }
        public IRelayCommand NavigateSeatingCommand { get; }
        public IRelayCommand NavigateSnapshotsCommand { get; }
        public IRelayCommand NavigatePluginsCommand { get; }

        public MainShellViewModel(IServiceProvider serviceProvider)
        {
            NavigateDataManagementCommand = new RelayCommand(() => CurrentView = serviceProvider.GetRequiredService<DataManagementViewModel>());
            NavigateVenueConfigCommand = new RelayCommand(() => CurrentView = serviceProvider.GetRequiredService<VenueConfigurationViewModel>());
            NavigateStrategyConfigCommand = new RelayCommand(() => CurrentView = serviceProvider.GetRequiredService<StrategyConfigurationViewModel>());
            NavigateSeatingCommand = new RelayCommand(() => CurrentView = serviceProvider.GetRequiredService<SeatingArrangementViewModel>());
            NavigateSnapshotsCommand = new RelayCommand(() => CurrentView = serviceProvider.GetRequiredService<SnapshotHistoryViewModel>());
            NavigatePluginsCommand = new RelayCommand(() => CurrentView = serviceProvider.GetRequiredService<PluginManagementViewModel>());
            CurrentView = serviceProvider.GetRequiredService<MainWindowViewModel>();
        }
    }
}