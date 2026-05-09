using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class SeatDisplayItem : ObservableObject
{
    // ── 几何（不可变） ──
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 22;
    public double Height { get; init; } = 18;
    public CornerRadius CornerRadius { get; init; } = new(3);
    public string SeatId { get; init; } = string.Empty;
    public string SeatLabel { get; init; } = string.Empty;

    // ── 座位类型标签（固定/前排） ──
    public bool IsFrontRow { get; init; }

    // ── 占有者（可观察） ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    [NotifyPropertyChangedFor(nameof(TooltipText))]
    [NotifyPropertyChangedFor(nameof(IsOccupied))]
    private string? _studentName;

    [ObservableProperty]
    private string? _studentId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    [NotifyPropertyChangedFor(nameof(TooltipText))]
    private bool _isOccupied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BorderBrush))]
    private bool _isFixed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(BorderBrush))]
    private SeatOccupancyStatus _occupancyStatus;

    // ── 交互状态 ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BorderBrush))]
    [NotifyPropertyChangedFor(nameof(BackgroundBrush))]
    private bool _isSelectedForSwap;

    // ── 计算属性 ──
    public string DisplayText => IsOccupied ? (StudentName ?? "") : SeatLabel;

    // ── Tooltip ──
    public string TooltipText => IsOccupied
        ? $"{StudentName} - {SeatLabel}"
        : $"空座位 - {SeatLabel}";

    // ── 颜色 ──
    private static readonly SolidColorBrush EmptyBg = new(Color.FromArgb(0x20, 0xA0, 0xA0, 0xA0));
    private static readonly SolidColorBrush EmptyBorder = new(Color.FromArgb(0x80, 0xA0, 0xA0, 0xA0));
    private static readonly SolidColorBrush OccupiedBg = new(Color.FromArgb(0x20, 0x16, 0xA3, 0x4A));
    private static readonly SolidColorBrush OccupiedBorder = new(Color.FromArgb(0xFF, 0x16, 0xA3, 0x4A));
    private static readonly SolidColorBrush FixedBg = new(Color.FromArgb(0x20, 0x25, 0x63, 0xEB));
    private static readonly SolidColorBrush FixedBorder = new(Color.FromArgb(0xFF, 0x25, 0x63, 0xEB));
    private static readonly SolidColorBrush SwapBg = new(Color.FromArgb(0x40, 0xF9, 0x73, 0x16));
    private static readonly SolidColorBrush SwapBorder = new(Color.FromArgb(0xFF, 0xF9, 0x73, 0x16));

    public IBrush BackgroundBrush => IsSelectedForSwap ? SwapBg : OccupancyStatus switch
    {
        SeatOccupancyStatus.Occupied => OccupiedBg,
        SeatOccupancyStatus.Fixed => FixedBg,
        _ => EmptyBg
    };

    public IBrush BorderBrush => IsSelectedForSwap ? SwapBorder : OccupancyStatus switch
    {
        SeatOccupancyStatus.Occupied => OccupiedBorder,
        SeatOccupancyStatus.Fixed => FixedBorder,
        _ => EmptyBorder
    };
}

public enum SeatOccupancyStatus
{
    Empty,
    Occupied,
    Fixed
}
