using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 页面侧栏/列表面板的「桌面内联 ↔ 紧凑右抽屉 ↔ 手机全屏覆盖」共享状态（M3/M7）。
/// 断点来自 <see cref="IShellLayoutService"/>：桌面时面板在指定列内联；
/// 紧凑档（600–900px）切到内容列做右侧覆盖抽屉（配合页面内遮罩点击关闭）；
/// 手机档（≤600px）全宽覆盖（无遮罩，配合面板头部返回按钮），切页时由页面收起。
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

    /// <summary>紧凑/手机档下面板是否展开。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visible))]
    [NotifyPropertyChangedFor(nameof(MaskVisible))]
    public partial bool IsOpen { get; set; }

    /// <summary>桌面常驻；窄屏仅面板展开时可见。</summary>
    public bool Visible => !_layout.IsNarrow || IsOpen;

    /// <summary>仅紧凑档且抽屉展开时显示遮罩（手机档全屏覆盖无遮罩）。</summary>
    public bool MaskVisible => _layout.Tier == LayoutTier.Compact && IsOpen;

    /// <summary>面板宽度：桌面内联 / 紧凑抽屉固定宽 / 手机档 NaN（全宽拉伸）。</summary>
    public double Width => _layout.Tier switch
    {
        LayoutTier.Phone => double.NaN,
        LayoutTier.Compact => _drawerWidth,
        _ => _desktopWidth
    };

    /// <summary>面板所在列（桌面内联列；窄屏切到内容列覆盖）。</summary>
    public int Column => _layout.IsNarrow ? 1 : _desktopColumn;

    /// <summary>面板水平对齐（紧凑右对齐覆盖；桌面/手机档拉伸）。</summary>
    public global::Avalonia.Layout.HorizontalAlignment Alignment => _layout.Tier switch
    {
        LayoutTier.Compact => global::Avalonia.Layout.HorizontalAlignment.Right,
        _ => global::Avalonia.Layout.HorizontalAlignment.Stretch
    };

    /// <summary>面板层级（窄屏需在内容/遮罩之上）。</summary>
    public int ZIndex => _layout.Tier switch
    {
        LayoutTier.Phone => 32,
        LayoutTier.Compact => 30,
        _ => 0
    };

    /// <summary>面板描边（手机档全屏无边；紧凑抽屉左边线）。</summary>
    public global::Avalonia.Thickness BorderThickness => _layout.Tier switch
    {
        LayoutTier.Phone => new global::Avalonia.Thickness(0),
        LayoutTier.Compact => _drawerBorder,
        _ => _desktopBorder
    };

    /// <summary>是否手机档全屏覆盖（面板内容据此显示返回头部）。</summary>
    public bool IsFullScreen => _layout.IsPhone;

    private void OnLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IShellLayoutService.Tier)) return;
        if (!_layout.IsNarrow) IsOpen = false;
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(MaskVisible));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Column));
        OnPropertyChanged(nameof(Alignment));
        OnPropertyChanged(nameof(ZIndex));
        OnPropertyChanged(nameof(BorderThickness));
        OnPropertyChanged(nameof(IsFullScreen));
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void Close() => IsOpen = false;
}
