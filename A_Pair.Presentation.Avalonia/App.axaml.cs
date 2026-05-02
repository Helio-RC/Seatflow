using System;
using System.Linq;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Presentation.Avalonia.Services;
using A_Pair.Presentation.Avalonia.ViewModels;
using A_Pair.Presentation.Avalonia.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using AvaloniaApplication = Avalonia.Application;

namespace A_Pair.Presentation.Avalonia
{
    public partial class App : AvaloniaApplication
    {
        private readonly IServiceProvider _serviceProvider;

        public App(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        private async Task EnsureDefaultSettingsAsync()
        {
            try
            {
                var facade = _serviceProvider.GetRequiredService<IApplicationFacade>();
                await facade.SaveAppSettingsAsync(new Core.Models.AppSettings());
            }
            catch
            {
                // 首次保存失败可忽略
            }
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
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

                // 首次启动时自动创建默认配置文件
                _ = EnsureDefaultSettingsAsync();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
