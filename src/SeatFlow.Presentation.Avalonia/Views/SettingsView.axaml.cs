using System;
using System.ComponentModel;
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
    /// 行高独立；卡片宽度按可用宽度 / 列数（桌面两列、窄屏单列）在 code-behind 计算。
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
        private SettingsViewModel? _observedViewModel;

        public SettingsView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += (_, _) =>
            {
                ObserveViewModel(DataContext as SettingsViewModel);
                UpdateCardLayout();
            };
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == BoundsProperty)
                UpdateCardLayout();
        }

        /// <summary>按可用宽度与列数（布局档位）更新卡片宽度。</summary>
        private void UpdateCardLayout()
        {
            if (DataContext is not SettingsViewModel vm) return;

            // 可用宽度 = 视口扣掉页边距，再被 StackPanel.MaxWidth 钳制
            // （MaxWidth 作用于控件自身，不含 Margin，故先扣边距再取 Min）。
            var available = Math.Min(Bounds.Width - PagePadding, ContentMaxWidth);
            if (available <= 0) return;

            var columns = Math.Max(1, vm.CardColumns);
            var cardWidth = (available - (CardSpacing * (columns - 1))) / columns;

            // 逐卡片设置 Width 而非 WrapPanel.ItemWidth：
            // Avalonia 的 WrapPanel 在设置了 ItemWidth 时，会把该固定宽度也分配给 IsVisible=false
            // 的子项（仅跳过间距），导致隐藏卡片仍占用一整列槽位、原位置留下空洞——
            // 网页端隐藏「存储 / 更新」两张卡片即触发此错位。
            // 改为逐卡片 Width 后，隐藏卡片的 DesiredSize 归零，既不占宽度也不占间距。
            foreach (var child in SettingsCards.Children)
                child.Width = cardWidth;
        }

        /// <summary>订阅/退订 VM 属性变更（列数随布局档位切换时无需等待 Bounds 变化）。</summary>
        private void ObserveViewModel(SettingsViewModel? vm)
        {
            if (ReferenceEquals(_observedViewModel, vm)) return;

            if (_observedViewModel != null)
                _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;

            _observedViewModel = vm;

            if (vm != null)
                vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.CardColumns))
                UpdateCardLayout();
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            _lifecycleCts?.Cancel();
            _lifecycleCts?.Dispose();
            _lifecycleCts = new CancellationTokenSource();

            ObserveViewModel(DataContext as SettingsViewModel);
            UpdateCardLayout();

            if (DataContext is SettingsViewModel vm)
                _ = vm.OnEnterAsync(_lifecycleCts.Token);
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            ObserveViewModel(null);

            _lifecycleCts?.Cancel();
            _lifecycleCts?.Dispose();
            _lifecycleCts = null;

            if (DataContext is SettingsViewModel vm)
                _ = vm.OnLeaveAsync();
        }
    }
}
