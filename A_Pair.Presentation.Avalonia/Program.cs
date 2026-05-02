using System;
using Avalonia;
using A_Pair.Application.Services;
using A_Pair.Presentation.Avalonia.Services;
using A_Pair.Presentation.Avalonia.ViewModels;
using A_Pair.Presentation.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;

namespace A_Pair.Presentation.Avalonia
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var services = new ServiceCollection();
            services.AddA_PairApplication("AppData", "Plugins");

            // 注册导航服务
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IFileService, FileService>();
            services.AddSingleton<IDialogService, DialogService>();

            // 注册 ViewModels
            services.AddSingleton<MainWindow>();
            services.AddSingleton<MainShellViewModel>();
            services.AddTransient<DataManagementViewModel>();
            services.AddTransient<VenueConfigurationViewModel>();
            services.AddTransient<StrategyConfigurationViewModel>();
            services.AddTransient<SeatingArrangementViewModel>();
            services.AddTransient<SnapshotHistoryViewModel>();
            services.AddTransient<PluginManagementViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<AboutViewModel>();

            var serviceProvider = services.BuildServiceProvider();
            BuildAvaloniaApp(serviceProvider)
                .StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp(IServiceProvider serviceProvider)
            => AppBuilder.Configure(() => new App(serviceProvider))
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}
