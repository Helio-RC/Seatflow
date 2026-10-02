using System;
using System.ComponentModel;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 页面繁忙状态作用域（03 §6「Busy/Loading 统一」）：
/// 通过 <see cref="Begin"/> 获取一次性作用域，页面遮罩与命令禁用统一由 <see cref="IsBusy"/> 驱动。
/// 支持嵌套（计数），作用域 Dispose 后自动归零。
/// </summary>
public interface IBusyScope : INotifyPropertyChanged
{
    /// <summary>是否有任一进行中的繁忙作用域。</summary>
    bool IsBusy { get; }

    /// <summary>当前繁忙提示文案（可选）。</summary>
    string? Message { get; }

    /// <summary>开始一个繁忙作用域；Dispose 结束时释放。</summary>
    IDisposable Begin(string? message = null);
}
