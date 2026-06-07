using CommunityToolkit.Mvvm.ComponentModel;

namespace A_Pair.Presentation.Avalonia.ViewModels;

/// <summary>
/// 座位定位器 ViewModel。根据布局类型显示不同的定位控件。
/// </summary>
public partial class SeatPositionPickerViewModel : ViewModelBase
{
    /// <summary>布局类型：Grid / Polar / Freeform。</summary>
    [ObservableProperty]
    private string _layoutType = "Grid";

    // ── Grid ──

    [ObservableProperty]
    private int _row = 1;

    [ObservableProperty]
    private int _column = 1;

    // ── Polar ──

    [ObservableProperty]
    private int _ring = 1;

    [ObservableProperty]
    private double _angle;

    // ── Freeform ──

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    // ── 动态范围（由会场 LayoutMetadata 决定，默认值为无约束时的上限） ──

    /// <summary>Grid 布局的最大行号。</summary>
    [ObservableProperty]
    private int _gridMaxRow = 99;

    /// <summary>Grid 布局的最大列号。</summary>
    [ObservableProperty]
    private int _gridMaxColumn = 99;

    /// <summary>Polar 布局的最大环号。</summary>
    [ObservableProperty]
    private int _polarMaxRing = 99;

    /// <summary>Freeform 布局的最大 X 坐标。</summary>
    [ObservableProperty]
    private double _freeformMaxX = 9999;

    /// <summary>Freeform 布局的最大 Y 坐标。</summary>
    [ObservableProperty]
    private double _freeformMaxY = 9999;

    // ── 类型判断 ──

    public bool IsGrid => LayoutType == "Grid";
    public bool IsPolar => LayoutType == "Polar";
    public bool IsFreeform => LayoutType == "Freeform";

    partial void OnLayoutTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsGrid));
        OnPropertyChanged(nameof(IsPolar));
        OnPropertyChanged(nameof(IsFreeform));
    }
}
