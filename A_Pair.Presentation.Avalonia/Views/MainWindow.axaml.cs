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
            // 延迟关闭，等 IO 完成后再真正关闭窗口
            e.Cancel = true;

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
                await Task.Run(async () => await SaveWindowStateAsync(facade, state));
            }

            Close();
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
