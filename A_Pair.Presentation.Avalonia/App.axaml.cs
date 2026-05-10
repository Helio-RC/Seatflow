using System;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Presentation.Avalonia.Services;
using A_Pair.Presentation.Avalonia.ViewModels;
using A_Pair.Presentation.Avalonia.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AvaloniaApplication = Avalonia.Application;

namespace A_Pair.Presentation.Avalonia
{
    public partial class App : AvaloniaApplication
    {
        private readonly IServiceProvider _serviceProvider;

        internal IServiceProvider ServiceProvider => _serviceProvider;

        public App (IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public override void Initialize ()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async Task RestoreSettingsAsync ()
        {
            try
            {
                var facade = _serviceProvider.GetRequiredService<IApplicationFacade>();
                var settings = await facade.LoadAppSettingsAsync();
                ApplyTheme(settings.Theme);

                // 恢复窗口状态
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    && desktop.MainWindow is { } window)
                {
                    var ws = settings.WindowState;
                    if (ws.Width > 0 && ws.Height > 0)
                    {
                        window.Width = ws.Width;
                        window.Height = ws.Height;
                    }
                    if (ws.Left != 0 || ws.Top != 0)
                        window.Position = new PixelPoint((int)ws.Left, (int)ws.Top);

                    if (ws.IsMaximized)
                        window.WindowState = WindowState.Maximized;
                }

                // 无文件时创建默认配置文件
                await facade.SaveAppSettingsAsync(settings);
            }
            catch
            {
                // 读取/写入失败忽略
            }
        }

        private void ApplyTheme (ThemeMode mode)
        {
            RequestedThemeVariant = mode switch
            {
                ThemeMode.Light => ThemeVariant.Light,
                ThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }

        public override void OnFrameworkInitializationCompleted ()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainShell = _serviceProvider.GetRequiredService<MainShellViewModel>();
                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                mainWindow.DataContext = mainShell;
                desktop.MainWindow = mainWindow;

                _serviceProvider.GetRequiredService<IFileService>().SetTopLevel(mainWindow);
                _serviceProvider.GetRequiredService<IDialogService>().SetTopLevel(mainWindow);

                ViewModelBase.InitializeDialogService(_serviceProvider.GetRequiredService<IDialogService>());
                ViewModelBase.InitializeLogger(_serviceProvider.GetRequiredService<ILogger<ViewModelBase>>());

                // 启动看门狗，防止 UI 卡死无法退出
                WatchdogService.SetDialogService(_serviceProvider.GetRequiredService<IDialogService>());
                var watchdog = _serviceProvider.GetRequiredService<WatchdogService>();
                watchdog.Start();
                var pingTimer = new global::Avalonia.Threading.DispatcherTimer(
                    TimeSpan.FromSeconds(3),
                    global::Avalonia.Threading.DispatcherPriority.Background,
                    (_, _) => watchdog.Ping());
                pingTimer.Start();

                // 启动时恢复已保存的设置（主题、语言等）
                _ = RestoreSettingsAsync();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
