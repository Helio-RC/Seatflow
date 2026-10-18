using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Views;

/// <summary>
/// 人员名单页视图（虚拟化表格由共享 <see cref="Controls.StudentRosterTable"/> 承载；
/// 本类只桥接页面生命周期与紧凑数据集抽屉）。
/// 说明：当前 <c>NavigationService</c> 尚未调用 <c>IPageLifecycle</c>，
/// 因此在 Loaded/Unloaded 桥接页面生命周期（进入时加载、离开时取消在途任务）。
/// </summary>
public partial class MemberManagementView : UserControl
{
    private CancellationTokenSource? _lifecycleCts;

    public MemberManagementView()
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

        if (DataContext is MemberManagementViewModel vm)
            _ = vm.OnEnterAsync(_lifecycleCts.Token);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _lifecycleCts?.Cancel();
        _lifecycleCts?.Dispose();
        _lifecycleCts = null;

        if (DataContext is MemberManagementViewModel vm)
            _ = vm.OnLeaveAsync();
    }

    /// <summary>紧凑档数据集抽屉遮罩点击：关闭抽屉。</summary>
    private void OnDrawerMaskPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MemberManagementViewModel vm)
            vm.DatasetsPanel.CloseCommand.Execute(null);
    }
}
