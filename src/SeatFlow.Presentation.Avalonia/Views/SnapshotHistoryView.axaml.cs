using System.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Views
{
    /// <summary>
    /// 历史快照页视图（M4：Singleton + 显式刷新 + IPageLifecycle 桥接 + 画布预览 + 紧凑快照列表抽屉）。
    /// 说明：当前 <c>NavigationService</c> 尚未调用 <c>IPageLifecycle</c>，
    /// 因此在 Loaded/Unloaded 桥接页面生命周期（进入时加载、离开时取消在途任务）。
    /// </summary>
    public partial class SnapshotHistoryView : UserControl
    {
        private CancellationTokenSource? _lifecycleCts;

        public SnapshotHistoryView()
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

            if (DataContext is SnapshotHistoryViewModel vm)
                _ = vm.OnEnterAsync(_lifecycleCts.Token);
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            _lifecycleCts?.Cancel();
            _lifecycleCts?.Dispose();
            _lifecycleCts = null;

            if (DataContext is SnapshotHistoryViewModel vm)
                _ = vm.OnLeaveAsync();
        }

        /// <summary>紧凑模式快照列表抽屉遮罩点击：关闭抽屉。</summary>
        private void OnDrawerMaskPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is SnapshotHistoryViewModel vm)
                vm.ListPanel.IsOpen = false;
        }
    }
}
