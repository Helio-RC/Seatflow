using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace A_Pair.Presentation.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow ()
        {
            InitializeComponent();
        }

        protected override void OnPropertyChanged (AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == BoundsProperty && DataContext is ViewModels.MainShellViewModel vm)
                vm.OnWindowWidthChanged(Bounds.Width);
        }

        protected override async void OnClosing (WindowClosingEventArgs e)
        {
            // 在 UI 线程捕获窗口状态
            var state = new WindowStateSettings
            {
                Left = Position.X,
                Top = Position.Y,
                Width = Width,
                Height = Height,
                IsMaximized = WindowState == WindowState.Maximized
            };

            var appInstance = global::Avalonia.Application.Current as App;
            if (appInstance is not null)
            {
                var facade = appInstance.ServiceProvider.GetRequiredService<IApplicationFacade>();
                // IO 放到线程池，避免 UI 线程死锁
                await Task.Run(async () => await SaveWindowStateAsync(facade, state));
            }

            base.OnClosing(e);
        }

        private static async Task SaveWindowStateAsync (IApplicationFacade facade , WindowStateSettings state)
        {
            try
            {
                var settings = await facade.LoadAppSettingsAsync();
                settings.WindowState = state;
                await facade.SaveAppSettingsAsync(settings);
            }
            catch
            {
                // 保存失败静默忽略
            }
        }
    }
}
