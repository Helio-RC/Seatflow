using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 布局档位（以窗口/视口宽度驱动）：
/// <list type="bullet">
/// <item><see cref="Desktop"/>：宽度 &gt; 900px，侧栏/页面面板内联；</item>
/// <item><see cref="Compact"/>：600px &lt; 宽度 ≤ 900px，侧栏与面板转覆盖抽屉；</item>
/// <item><see cref="Phone"/>：宽度 ≤ 600px，底部 Tab 导航、页面面板全屏覆盖。</item>
/// </list>
/// </summary>
public enum LayoutTier
{
    /// <summary>桌面档（&gt; 900px）。</summary>
    Desktop,

    /// <summary>紧凑档（600–900px，平板 / 窄窗口）。</summary>
    Compact,

    /// <summary>手机档（≤ 600px）。</summary>
    Phone
}

/// <summary>响应式断点常量（px）与档位解析。</summary>
public static class LayoutBreakpoints
{
    /// <summary>紧凑/桌面分界：宽度 ≤ 900px 进入紧凑档。</summary>
    public const double Compact = 900;

    /// <summary>手机/紧积分界：宽度 ≤ 600px 进入手机档。</summary>
    public const double Phone = 600;

    /// <summary>由宽度解析布局档位（调用方负责忽略非正宽度）。</summary>
    public static LayoutTier Resolve(double width) =>
        width <= Phone ? LayoutTier.Phone
        : width <= Compact ? LayoutTier.Compact
        : LayoutTier.Desktop;
}

/// <summary>
/// 外壳布局状态（M3，响应式三档扩展）：以窗口宽度驱动的全局布局档位。
/// 外壳（<see cref="ViewModels.MainShellViewModel"/>）在窗口宽度变化时写入；
/// 页面 ViewModel 读取该状态以切换「内联面板 ↔ 抽屉 ↔ 全屏面板」布局。
/// </summary>
public interface IShellLayoutService : INotifyPropertyChanged
{
    /// <summary>当前布局档位。</summary>
    LayoutTier Tier { get; set; }

    /// <summary>是否为窄屏（紧凑或手机档）。</summary>
    bool IsNarrow { get; }

    /// <summary>是否为手机档（≤ 600px）。</summary>
    bool IsPhone { get; }
}

/// <summary><see cref="IShellLayoutService"/> 的默认实现（DI 单例）。</summary>
public sealed partial class ShellLayoutService : ObservableObject, IShellLayoutService
{
    [ObservableProperty]
    public partial LayoutTier Tier { get; set; }

    /// <inheritdoc />
    public bool IsNarrow => Tier != LayoutTier.Desktop;

    /// <inheritdoc />
    public bool IsPhone => Tier == LayoutTier.Phone;

    partial void OnTierChanged(LayoutTier value)
    {
        OnPropertyChanged(nameof(IsNarrow));
        OnPropertyChanged(nameof(IsPhone));
    }
}
