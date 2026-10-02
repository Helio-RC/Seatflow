using System;
using System.Threading;
using System.Threading.Tasks;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 对话框门：保证同一时刻只有一个模态流程在执行（替代各页面手写的
/// <c>_dialogLock</c> + <c>Interlocked.CompareExchange</c> + <c>Task.Delay(150)</c>）。
/// 已在执行时后续调用立即返回默认值 / false，调用方可直接 return。
/// </summary>
public interface IDialogGate
{
    /// <summary>当前是否有模态流程在占用门。</summary>
    bool IsBusy { get; }

    /// <summary>尝试执行返回值的模态流程；门被占用时返回 default 且不执行。</summary>
    Task<T?> RunAsync<T>(Func<Task<T>> action);

    /// <summary>尝试执行无返回值的模态流程；门被占用时返回 false 且不执行。</summary>
    Task<bool> RunAsync(Func<Task> action);
}

/// <summary>基于 Interlocked 的对话框门实现（DI 单例）。</summary>
public sealed class DialogGate : IDialogGate
{
    private int _busy;

    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    public async Task<T?> RunAsync<T>(Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return default;

        try
        {
            return await action();
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public async Task<bool> RunAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return false;

        try
        {
            await action();
            return true;
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }
}
