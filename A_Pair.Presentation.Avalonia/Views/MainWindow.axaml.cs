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

        protected override void OnClosing (WindowClosingEventArgs e)
        {
            // fire-and-forget 保存窗口状态，不阻塞关闭
            var state = new WindowStateSettings
            {
                Left = Position.X,
                Top = Position.Y,
                Width = Width,
                Height = Height,
                IsMaximized = WindowState == WindowState.Maximized
            };

            _ = SaveWindowStateAsync(state);
            base.OnClosing(e);
        }

        private async Task SaveWindowStateAsync (WindowStateSettings state)
        {
            try
            {
                var appInstance = global::Avalonia.Application.Current as App;
                if (appInstance is null) return;
                var facade = appInstance.ServiceProvider.GetRequiredService<IApplicationFacade>();
                var settings = await facade.LoadAppSettingsAsync();
                settings.WindowState = state;
                await facade.SaveAppSettingsAsync(settings);
            }
            catch
            {
                // 静默忽略
            }
        }

    }
}
