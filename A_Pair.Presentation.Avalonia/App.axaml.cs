using A_Pair.Presentation.Avalonia.ViewModels;
using A_Pair.Presentation.Avalonia.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using A_Pair.Application.Services;
using System.Linq;

namespace A_Pair.Presentation.Avalonia
{
    public partial class App : global::Avalonia.Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Configure DI container for UI and set MainWindow DataContext to shell view model
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            // register application services (facade, repositories, strategies, exporters)
            services.AddA_PairApplication("Snapshots");
            // register UI viewmodels with DI
            services.AddSingleton<A_Pair.Presentation.Avalonia.ViewModels.MainWindowViewModel>();
            services.AddSingleton<A_Pair.Presentation.Avalonia.ViewModels.DataManagementViewModel>();
            services.AddSingleton<A_Pair.Presentation.Avalonia.ViewModels.VenueConfigurationViewModel>();
            services.AddSingleton<A_Pair.Presentation.Avalonia.ViewModels.StrategyConfigurationViewModel>();
            services.AddSingleton<A_Pair.Presentation.Avalonia.ViewModels.SeatingArrangementViewModel>();
            services.AddSingleton<A_Pair.Presentation.Avalonia.ViewModels.SnapshotHistoryViewModel>();
            services.AddSingleton<A_Pair.Presentation.Avalonia.ViewModels.PluginManagementViewModel>();
            services.AddSingleton<A_Pair.Presentation.Avalonia.ViewModels.MainShellViewModel>();
            var sp = services.BuildServiceProvider();

            if (this.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                var shell = sp.GetRequiredService<A_Pair.Presentation.Avalonia.ViewModels.MainShellViewModel>();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = shell,
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}