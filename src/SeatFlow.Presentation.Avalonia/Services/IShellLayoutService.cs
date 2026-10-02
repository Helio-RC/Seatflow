using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 外壳布局状态（M3）：以窗口宽度驱动的全局紧凑断点。
/// 外壳（<see cref="ViewModels.MainShellViewModel"/>）在窗口宽度变化时写入；
/// 页面 ViewModel 读取该状态以切换「内联面板 ↔ 抽屉」布局（如排座工作台的三栏）。
/// </summary>
public interface IShellLayoutService : INotifyPropertyChanged
{
    /// <summary>是否处于紧凑模式（窗口宽度 ≤ 900px）。</summary>
    bool IsCompact { get; set; }
}

/// <summary><see cref="IShellLayoutService"/> 的默认实现（DI 单例）。</summary>
public sealed partial class ShellLayoutService : ObservableObject, IShellLayoutService
{
    [ObservableProperty]
    public partial bool IsCompact { get; set; }
}
