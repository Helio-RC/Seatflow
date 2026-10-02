using Avalonia.Controls;
using Avalonia.Interactivity;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Views;

/// <summary>
/// .seatsets 数据包导出/导入的数据类别选择对话框（桌面窗口）。
/// 内容与浏览器端 overlay 共用 <see cref="SeatSetsSelectionContent"/>。
/// </summary>
internal partial class SeatSetsSelectionWindow : Window
{
    private readonly SeatSetsSelectionViewModel _viewModel;

    public SeatSetsSelectionWindow()
    {
        InitializeComponent();
        _viewModel = new SeatSetsSelectionViewModel();
        DataContext = _viewModel;
        SelectionContent.Completed += confirmed => Close(confirmed);
    }

    /// <summary>是否为导出模式（false 表示导入模式）。</summary>
    public bool IsExport
    {
        get => _viewModel.IsExport;
        set => _viewModel.IsExport = value;
    }

    /// <summary>获取用户的类别选择结果。</summary>
    public SeatSetsSelectionViewModel ViewModel => _viewModel;

    /// <summary>
    /// 根据可用类别预填复选框（用于导入模式）。
    /// </summary>
    public void SetAvailableCategories(
        bool appSettings, bool venues, bool rosters,
        bool snapshots, bool strategyConfig)
    {
        _viewModel.SetAvailableCategories(appSettings, venues, rosters, snapshots, strategyConfig);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Title = _viewModel.Title;
    }
}
