using System;
using System.IO;
using System.Threading;
using A_Pair.Application.Services;
using A_Pair.Infrastructure.Providers;
using A_Pair.Presentation.Avalonia.Services;
using A_Pair.Presentation.Avalonia.ViewModels;
using A_Pair.Presentation.Avalonia.Views;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace A_Pair.Presentation.Avalonia
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main (string[] args)
        {
            using var mutex = new Mutex(true, @"Global\A_Pair_SeatingArrangement", out bool isFirstInstance);

            var services = new ServiceCollection();
            services.AddA_PairApplication("AppData" , "Plugins");

            // 注册导航服务
            services.AddSingleton<INavigationService , NavigationService>();
            services.AddSingleton<IFileService , FileService>();
            services.AddSingleton<IDialogService , DialogService>();
            services.AddSingleton<WatchdogService>();

            // 注册文件日志
            var logDir = Path.Combine("AppData", "Logs");
            services.AddLogging(builder =>
            {
                builder.AddProvider(new FileLoggerProvider(logDir, LogLevel.Information, 5 * 1024 * 1024, 10));
            });

            // 注册 ViewModels
            services.AddSingleton<MainWindow>();
            services.AddSingleton<MainShellViewModel>();
            services.AddSingleton<HomeViewModel>();
            services.AddSingleton<DataManagementViewModel>();
            services.AddSingleton<VenueConfigurationViewModel>();
            services.AddSingleton<FreeformManagementViewModel>();
            services.AddSingleton<StrategyConfigurationViewModel>();
            services.AddSingleton<SeatingArrangementViewModel>();
            services.AddSingleton<SnapshotHistoryViewModel>();
            services.AddSingleton<PluginManagementViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<AboutViewModel>();

            var serviceProvider = services.BuildServiceProvider();
            BuildAvaloniaApp(serviceProvider, isFirstInstance)
                .StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp (IServiceProvider serviceProvider, bool isFirstInstance)
            => AppBuilder.Configure(() => new App(serviceProvider, isFirstInstance))
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}
