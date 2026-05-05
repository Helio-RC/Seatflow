using System;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Presentation.Avalonia.Services;
using A_Pair.Presentation.Avalonia.ViewModels;
using A_Pair.Presentation.Avalonia.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using AvaloniaApplication = Avalonia.Application;

namespace A_Pair.Presentation.Avalonia
{
    public partial class App : AvaloniaApplication
    {
        private readonly IServiceProvider _serviceProvider;

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

                // 启动时恢复已保存的设置（主题、语言等）
                _ = RestoreSettingsAsync();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
