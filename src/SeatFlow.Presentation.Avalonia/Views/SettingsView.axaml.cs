using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Views
{
    /// <summary>
    /// 设置页视图（M5：分组卡片网格 + 全令牌化；Loaded/Unloaded 桥接 <c>IPageLifecycle</c>）。
    /// M6 后修复：卡片容器由 UniformGrid（全网格等高 → 短卡片大片空白）改为 WrapPanel，
    /// 行高独立；卡片宽度按可用宽度 / 列数（桌面两列、≤900px 单列）在 code-behind 计算。
    /// </summary>
    public partial class SettingsView : UserControl
    {
        /// <summary>内容区最大宽度（与 XAML StackPanel.MaxWidth 一致）。</summary>
        private const double ContentMaxWidth = 1100;

        /// <summary>页面内边距（SfPagePadding）两侧合计。</summary>
        private const double PagePadding = 32;

        /// <summary>卡片间距（SfSpace4）。</summary>
        private const double CardSpacing = 16;

        private CancellationTokenSource? _lifecycleCts;

        public SettingsView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += (_, _) => UpdateCardLayout();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == BoundsProperty)
                UpdateCardLayout();
        }

        /// <summary>按可用宽度与列数（紧凑断点）更新卡片宽度。</summary>
        private void UpdateCardLayout()
        {
            if (DataContext is not SettingsViewModel vm) return;

            var available = Math.Min(Bounds.Width, ContentMaxWidth) - PagePadding;
            if (available <= 0) return;

            var columns = Math.Max(1, vm.CardColumns);
            SettingsCards.ItemWidth = (available - (CardSpacing * (columns - 1))) / columns;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            _lifecycleCts?.Cancel();
            _lifecycleCts?.Dispose();
            _lifecycleCts = new CancellationTokenSource();

            UpdateCardLayout();

            if (DataContext is SettingsViewModel vm)
                _ = vm.OnEnterAsync(_lifecycleCts.Token);
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            _lifecycleCts?.Cancel();
            _lifecycleCts?.Dispose();
            _lifecycleCts = null;

            if (DataContext is SettingsViewModel vm)
                _ = vm.OnLeaveAsync();
        }
    }
}
