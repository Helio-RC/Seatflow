using System;
using System.ComponentModel;
using System.Threading.Tasks;
using A_Pair.Core.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace A_Pair.Presentation.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        private ViewModels.MainShellViewModel? _shell;
        private PageTransitionType _transitionType = PageTransitionType.CrossFade;
        private volatile bool _animating;

        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_shell != null)
                _shell.PropertyChanged -= OnShellPropertyChanged;
            _shell = DataContext as ViewModels.MainShellViewModel;
            if (_shell != null)
            {
                _shell.PropertyChanged += OnShellPropertyChanged;
                _transitionType = _shell.CurrentTransitionType;
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == BoundsProperty && _shell != null)
                _shell.OnWindowWidthChanged(Bounds.Width);
        }

        private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.MainShellViewModel.CurrentTransitionType))
            {
                _transitionType = _shell!.CurrentTransitionType;
            }
            else if (e.PropertyName == nameof(ViewModels.MainShellViewModel.CurrentViewModel))
            {
                _ = RunPageTransitionAsync();
            }
        }

        private async Task RunPageTransitionAsync()
        {
            if (_animating) return;
            _animating = true;

            try
            {
                var type = _transitionType;

                if (type == PageTransitionType.None)
                    return;

                // Phase 1: 遮罩淡入，同时旧内容滑出
                LoadingOverlay.Opacity = 1;
                LoadingOverlay.IsHitTestVisible = true;

                if (type is PageTransitionType.SlideHorizontal or PageTransitionType.Composite)
                    PageHost.RenderTransform = new TranslateTransform(-60, 0);
                else if (type == PageTransitionType.SlideVertical)
                    PageHost.RenderTransform = new TranslateTransform(0, -40);

                // 等待遮罩淡入 + 滑出动画完成
                await Task.Delay(200);

                // Phase 2: 内容已切换，给新页布局时间
                await Task.Delay(50);

                // Phase 3: 新内容从右侧滑入，同时遮罩淡出
                if (type is PageTransitionType.SlideHorizontal or PageTransitionType.Composite)
                    PageHost.RenderTransform = new TranslateTransform(60, 0);
                else if (type == PageTransitionType.SlideVertical)
                    PageHost.RenderTransform = new TranslateTransform(0, 40);

                // 下一帧立即回弹到中心 => 过渡动画驱动 60→0
                await Task.Delay(16);
                PageHost.RenderTransform = new TranslateTransform(0, 0);

                LoadingOverlay.Opacity = 0;
                LoadingOverlay.IsHitTestVisible = false;

                // 等滑入 + 淡出完成
                await Task.Delay(250);
            }
            finally
            {
                _animating = false;
            }
        }
    }
}
