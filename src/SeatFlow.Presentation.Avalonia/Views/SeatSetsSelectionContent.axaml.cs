using System;
using Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Views;

/// <summary>
/// .seatsets 数据类别选择内容（五个类别复选框 + 全选/取消全选 + 确认/取消）。
/// 桌面端由 <see cref="SeatSetsSelectionWindow"/> 承载，浏览器端由 WebDialogService
/// 以 overlay 方式承载，共用同一 UI 与 <see cref="SeatSetsSelectionViewModel"/>。
/// </summary>
internal partial class SeatSetsSelectionContent : UserControl
{
    /// <summary>确认（true = 至少选择一项）/ 取消（false）。</summary>
    public event Action<bool>? Completed;

    public SeatSetsSelectionContent()
    {
        InitializeComponent();

        ConfirmButton.Click += (_, _) => Completed?.Invoke(ViewModel.IsAnySelected);
        CancelButton.Click += (_, _) => Completed?.Invoke(false);
        ToggleAllButton.Click += (_, _) => ViewModel.ToggleAllCommand.Execute(null);
    }

    /// <summary>选择 ViewModel（调用方需先设置 DataContext）。</summary>
    public SeatSetsSelectionViewModel ViewModel =>
        (SeatSetsSelectionViewModel)DataContext!;
}
