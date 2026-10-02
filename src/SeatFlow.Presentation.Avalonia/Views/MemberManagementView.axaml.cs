using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Views;

/// <summary>
/// 人员名单页视图（M4：虚拟化表格 + 行内「显示/编辑」切换 + IPageLifecycle 桥接 + 紧凑数据集抽屉）。
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

    /// <summary>点击行（显示态）进入行内编辑；编辑控件上的点击不触发。</summary>
    private void OnStudentRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is TextBox or ComboBox or CheckBox or Button) return;
        if (sender is Border { DataContext: StudentRowViewModel row }
            && !row.IsEditing
            && DataContext is MemberManagementViewModel vm)
        {
            vm.BeginEditCommand.Execute(row);
        }
    }

    /// <summary>
    /// 行内编辑键盘（M5）：
    /// Enter 提交并退出编辑态；Esc 回滚到进入编辑态前的值并退出（行内任意编辑控件均生效）。
    /// Tab 依赖 Avalonia 默认焦点遍历（姓名 → 身高 → 性别 → 前排 → 操作按钮）。
    /// </summary>
    private void OnRowEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: StudentRowViewModel row }
            || DataContext is not MemberManagementViewModel vm)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                vm.EndEditCommand.Execute(row);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.CancelEditCommand.Execute(row);
                e.Handled = true;
                break;
        }
    }

    /// <summary>紧凑模式数据集抽屉遮罩点击：关闭抽屉。</summary>
    private void OnDrawerMaskPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MemberManagementViewModel vm)
            vm.DatasetsPanel.CloseCommand.Execute(null);
    }

    /// <summary>新增行 Name 文本框按 Enter 时触发 AddNewStudentCommand。</summary>
    public void OnNewStudentNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MemberManagementViewModel vm)
        {
            vm.AddNewStudentCommand.Execute(null);
            e.Handled = true;
        }
    }
}
