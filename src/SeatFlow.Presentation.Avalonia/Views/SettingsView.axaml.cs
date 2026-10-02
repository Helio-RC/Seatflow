using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Views
{
    /// <summary>
    /// 设置页视图（M5：分组卡片网格 + 全令牌化；Loaded/Unloaded 桥接 <c>IPageLifecycle</c>）。
    /// </summary>
    public partial class SettingsView : UserControl
    {
        private CancellationTokenSource? _lifecycleCts;

        public SettingsView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            _lifecycleCts?.Cancel();
            _lifecycleCts?.Dispose();
            _lifecycleCts = new CancellationTokenSource();

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
