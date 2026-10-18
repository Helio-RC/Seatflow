using CommunityToolkit.Mvvm.ComponentModel;
using SeatFlow.Presentation.Avalonia.Lang;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>过道选项（会场页与 OOBE 共用）：列/行标签、座位列索引与 tooltip。</summary>
public partial class AisleOption(string label, int seatColumn, string toolTip, bool selected = false) : ObservableObject
{
    public string Label { get; set; } = label;
    public int SeatColumn { get; set; } = seatColumn;
    public string ToolTip { get; set; } = toolTip;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = selected;
}

/// <summary>「每列行数」输入项：列号固定，行数留空 = 沿用全局「行数」。</summary>
public partial class ColumnRowCountOption(int column) : ObservableObject
{
    public int Column { get; } = column;

    public string Label => string.Format(Resources.Venue_ColumnRowsFmt, Column);

    [ObservableProperty]
    public partial int? Rows { get; set; }
}
