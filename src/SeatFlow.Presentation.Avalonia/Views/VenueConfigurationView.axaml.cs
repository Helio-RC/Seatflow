using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SeatFlow.Presentation.Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Views;

/// <summary>
/// 「会场与布局」页视图（M2：合并自由点管理 + 自绘预览）。
/// 说明：当前 <c>NavigationService</c> 尚未调用 <c>IPageLifecycle</c>，
/// 因此在 Loaded/Unloaded 桥接页面生命周期（进入时加载、离开时取消在途任务）。
/// </summary>
public partial class VenueConfigurationView : UserControl
{
    private CancellationTokenSource? _lifecycleCts;

    public VenueConfigurationView()
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

        if (DataContext is VenueConfigurationViewModel vm)
            _ = vm.OnEnterAsync(_lifecycleCts.Token);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _lifecycleCts?.Cancel();
        _lifecycleCts?.Dispose();
        _lifecycleCts = null;

        if (DataContext is VenueConfigurationViewModel vm)
            _ = vm.OnLeaveAsync();
    }

    private void OnPreviewSeatClicked(object? sender, SeatEventArgs e)
    {
        if (DataContext is VenueConfigurationViewModel vm && e.Seat is { } seat)
            vm.OnPreviewSeatClicked(seat);
    }
}
