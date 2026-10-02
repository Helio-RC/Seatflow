using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 页面侧栏/列表面板的「桌面内联 ↔ 紧凑右抽屉」共享状态（M4）。
/// 断点来自 <see cref="IShellLayoutService"/>（≤900px 紧凑）；桌面时面板在指定列内联，
/// 紧凑时切到内容列做右侧覆盖抽屉，配合页面内遮罩点击关闭，切页时由页面收起。
/// </summary>
public partial class SideDrawerState : ObservableObject
{
    private readonly IShellLayoutService _layout;
    private readonly int _desktopColumn;
    private readonly double _desktopWidth;
    private readonly double _drawerWidth;
    private readonly global::Avalonia.Thickness _desktopBorder;
    private readonly global::Avalonia.Thickness _drawerBorder;

    /// <param name="layout">外壳布局状态（DI 单例）。</param>
    /// <param name="desktopColumn">桌面内联所在列（如 0）。</param>
    /// <param name="desktopWidth">桌面内联宽度（如 248）。</param>
    /// <param name="drawerWidth">紧凑抽屉宽度（默认 320）。</param>
    /// <param name="desktopBorder">桌面描边（如右侧分隔线）。</param>
    /// <param name="drawerBorder">紧凑抽屉描边（默认左侧线）。</param>
    public SideDrawerState(
        IShellLayoutService layout,
        int desktopColumn,
        double desktopWidth,
        double drawerWidth = 320,
        global::Avalonia.Thickness? desktopBorder = null,
        global::Avalonia.Thickness? drawerBorder = null)
    {
        _layout = layout;
        _desktopColumn = desktopColumn;
        _desktopWidth = desktopWidth;
        _drawerWidth = drawerWidth;
        _desktopBorder = desktopBorder ?? new global::Avalonia.Thickness(0);
        _drawerBorder = drawerBorder ?? new global::Avalonia.Thickness(1, 0, 0, 0);
        _layout.PropertyChanged += OnLayoutChanged;
    }

    /// <summary>紧凑模式下抽屉是否展开。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visible))]
    [NotifyPropertyChangedFor(nameof(MaskVisible))]
    public partial bool IsOpen { get; set; }

    /// <summary>桌面常驻；紧凑仅抽屉展开时可见。</summary>
    public bool Visible => !_layout.IsCompact || IsOpen;

    /// <summary>紧凑且抽屉展开时显示遮罩。</summary>
    public bool MaskVisible => _layout.IsCompact && IsOpen;

    public double Width => _layout.IsCompact ? _drawerWidth : _desktopWidth;

    public int Column => _layout.IsCompact ? 1 : _desktopColumn;

    public global::Avalonia.Layout.HorizontalAlignment Alignment =>
        _layout.IsCompact ? global::Avalonia.Layout.HorizontalAlignment.Right : global::Avalonia.Layout.HorizontalAlignment.Stretch;

    public int ZIndex => _layout.IsCompact ? 30 : 0;

    public global::Avalonia.Thickness BorderThickness => _layout.IsCompact ? _drawerBorder : _desktopBorder;

    private void OnLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IShellLayoutService.IsCompact)) return;
        if (!_layout.IsCompact) IsOpen = false;
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(MaskVisible));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Column));
        OnPropertyChanged(nameof(Alignment));
        OnPropertyChanged(nameof(ZIndex));
        OnPropertyChanged(nameof(BorderThickness));
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void Close() => IsOpen = false;
}
