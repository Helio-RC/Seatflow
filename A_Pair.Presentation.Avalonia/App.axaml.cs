using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
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
    // AVLN3001: DI requires parameterized constructor, no public parameterless ctor
    #pragma warning disable AVLN3001
    public partial class App (IServiceProvider serviceProvider , bool isFirstInstance = true) : AvaloniaApplication
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider;
        private readonly bool _isFirstInstance = isFirstInstance;

        internal IServiceProvider ServiceProvider => _serviceProvider;

        public override void Initialize ()
        {
            ApplyLanguageFromSettings();
            AvaloniaXamlLoader.Load(this);
        }

        private async Task RestoreSettingsAsync ()
        {
            try
            {
                var facade = _serviceProvider.GetRequiredService<IApplicationFacade>();
                var settings = await facade.LoadAppSettingsAsync();
                ApplyTheme(settings.Theme);
                ApplyLanguage(settings.Language);

                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    && desktop.MainWindow is { } window)
                {
                    var ws = settings.WindowState;
                    // 先设置最大化状态，避免先设尺寸再最大化导致的闪烁
                    if (ws.IsMaximized)
                        window.WindowState = WindowState.Maximized;

                    // 非最大化时才恢复尺寸（最大化状态下尺寸由系统管理）
                    if (!ws.IsMaximized && ws.Width > 0 && ws.Height > 0)
                    {
                        window.Width = ws.Width;
                        window.Height = ws.Height;
                    }

                    // 始终恢复窗口位置，包括 (0,0)（它是合法的屏幕坐标）
                    window.Position = new PixelPoint((int)ws.Left , (int)ws.Top);
                }

                // 仅在配置文件不存在时创建默认文件，防止覆盖已有设置
                var repo = _serviceProvider.GetRequiredService<Core.Providers.IAppSettingsRepository>();
                if (repo is Infrastructure.Providers.JsonAppSettingsRepository jsonRepo
                    && !File.Exists(jsonRepo.SettingsFilePath))
                {
                    await facade.SaveAppSettingsAsync(settings);
                }
            }
            catch
            {
                // 读取/写入失败忽略
            }
        }

        private void ApplyLanguageFromSettings ()
        {
            try
            {
                var facade = _serviceProvider.GetRequiredService<IApplicationFacade>();
                // Task.Run 跳到线程池（无 SynchronizationContext），避免在 UI 调度器未就绪时死锁
                var settings = Task.Run(() => facade.LoadAppSettingsAsync(CancellationToken.None)).GetAwaiter().GetResult();
                ApplyLanguage(settings.Language);
            }
            catch
            {
                // 加载失败保持默认
            }
        }

        private static void ApplyLanguage (string language)
        {
            try
            {
                var culture = string.IsNullOrEmpty(language)
                    ? CultureInfo.InstalledUICulture
                    : new CultureInfo(language);

                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                Lang.Resources.Culture = culture;
            }
            catch (CultureNotFoundException)
            {
                // 无效的语言代码，保持系统默认
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

                // 启动检查
                _ = RunStartupChecksAsync(desktop);

                // 启动看门狗，防止 UI 卡死无法退出
                WatchdogService.SetDialogService(_serviceProvider.GetRequiredService<IDialogService>());
                var watchdog = _serviceProvider.GetRequiredService<WatchdogService>();
                watchdog.Start();
                var pingTimer = new global::Avalonia.Threading.DispatcherTimer(
                    TimeSpan.FromSeconds(3) ,
                    global::Avalonia.Threading.DispatcherPriority.Background ,
                    (_ , _) => watchdog.Ping());
                pingTimer.Start();

                // 全角字符输入转换（全角数字/符号 → 半角）
                Behaviors.ChineseInputNormalizer.Attach(mainWindow);

                // 退出看门狗：关闭信号发出后 20s 内未退出则强制终止
                desktop.ShutdownRequested += (_ , _) =>
                {
                    var exitLogger = _serviceProvider.GetRequiredService<ILogger<App>>();
                    Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(20));
                        exitLogger.LogWarning("程序退出超时（20s），强制终止进程");
                        Environment.Exit(0);
                    });
                };

                // 启动时恢复已保存的设置（主题、语言等）
                _ = RestoreSettingsAsync();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async Task RunStartupChecksAsync (IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dialog = _serviceProvider.GetRequiredService<IDialogService>();

            // 单实例检查
            if (!_isFirstInstance)
            {
                await DialogServiceShim.ShowWarningAsync(dialog ,
                    Lang.Resources.App_AlreadyRunning ,
                    Lang.Resources.App_AlreadyRunningMessage);
                desktop.Shutdown();
                return;
            }

            // 运行环境检查
            var settingsRepo = _serviceProvider.GetRequiredService<IAppSettingsRepository>();
            AppSettings settings;
            try
            {
                settings = await settingsRepo.LoadAsync();
            }
            catch
            {
                settings = new AppSettings();
            }

            if (!settings.SuppressEnvironmentWarning)
            {
                var (hasWarning , envMessage) = StartupGuard.CheckEnvironment();
                if (hasWarning)
                {
                    var result = await DialogServiceShim.ShowEnvironmentWarningAsync(dialog , envMessage);
                    if (result is 1) // "不再提醒" / "Don't remind again" 按钮
                    {
                        settings.SuppressEnvironmentWarning = true;
                        try
                        {
                            await settingsRepo.SaveAsync(settings);
                        }
                        catch
                        {
                            // 保存失败忽略
                        }
                    }
                }
            }
        }
    }

    internal static class DialogServiceShim
    {
        public static async Task ShowWarningAsync (IDialogService dialog , string title , string message)
        {
            try
            {
                await dialog.ShowWarningAsync(title , message);
            }
            catch
            {
                // 对话框显示失败时静默处理
            }
        }

        /// <returns>0=确定, 1=不再提醒, null=关闭窗口</returns>
        public static async Task<int?> ShowEnvironmentWarningAsync (IDialogService dialog , string message)
        {
            try
            {
                return await dialog.ShowMultiOptionAsync(
                    Lang.Resources.App_EnvironmentWarning ,
                    message ,
                    Lang.Resources.Common_OK ,
                    Lang.Resources.Common_DontRemind);
            }
            catch
            {
                return null;
            }
        }
    }
}
