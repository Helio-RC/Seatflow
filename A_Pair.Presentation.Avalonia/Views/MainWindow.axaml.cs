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
            SaveWindowState();
            base.OnClosing(e);
        }

        /// <summary>
        /// 在窗口关闭时将位置、大小和最大化状态写入 AppSettings。
        /// </summary>
        private void SaveWindowState ()
        {
            try
            {
                var appInstance = global::Avalonia.Application.Current as App;
                if (appInstance is null) return;
                var facade = appInstance.ServiceProvider.GetRequiredService<IApplicationFacade>();
                var settings = facade.LoadAppSettingsAsync().Result;
                settings.WindowState = new WindowStateSettings
                {
                    Left = Position.X,
                    Top = Position.Y,
                    Width = Width,
                    Height = Height,
                    IsMaximized = WindowState == WindowState.Maximized
                };
                facade.SaveAppSettingsAsync(settings).Wait();
            }
            catch
            {
                // 保存失败静默忽略
            }
        }
    }
}
