using System;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 页面 ViewModel 基类。
/// M0 起：对话框与日志通过构造函数注入（取消原静态可变状态 ViewModelBase.Dialog / _logger）。
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private readonly IDialogService _dialog;

    /// <param name="dialog">对话框服务（DI 单例；无对话框场景可传 <see cref="NullDialogService.Instance"/>）。</param>
    /// <param name="logger">该页面的日志记录器（可选）。</param>
    protected ViewModelBase(IDialogService dialog, ILogger? logger = null)
    {
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        Logger = logger;
    }

    /// <summary>该页面的对话框服务（由 DI 注入）。</summary>
    protected IDialogService Dialog => _dialog;

    /// <summary>该页面的日志记录器（可选）。</summary>
    protected ILogger? Logger { get; }

    /// <summary>在 try-catch 中执行操作，出错时弹窗并记录日志。</summary>
    protected async Task<bool> SafeExecuteAsync(Func<Task> action, string? errorTitle = null)
    {
        errorTitle ??= Resources.Common_OperationFailed;
        try
        {
            await action();
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "ViewModel 操作失败：{Title}", errorTitle);
            await Dialog.ShowErrorAsync(errorTitle, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 执行可取消操作：用户主动取消（<see cref="OperationCanceledException"/>）静默返回，不弹错误框；
    /// 其他异常仍按 <see cref="SafeExecuteAsync(Func{Task}, string?)"/> 语义记录并弹窗。
    /// 适用场景：长任务在页面离开时被取消（如生成座位安排）。
    /// </summary>
    protected async Task<bool> SafeCancelableAsync(Func<Task> action, string? errorTitle = null)
    {
        errorTitle ??= Resources.Common_OperationFailed;
        try
        {
            await action();
            return true;
        }
        catch (OperationCanceledException)
        {
            Logger?.LogDebug("操作被取消（用户主动取消/离开页面）：{Title}", errorTitle);
            return false;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "ViewModel 操作失败：{Title}", errorTitle);
            await Dialog.ShowErrorAsync(errorTitle, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 在带超时的 try-catch 中执行操作。超时后自动取消 CancellationToken 并弹窗提示，不会终止程序。
    /// </summary>
    /// <param name="action">接受 CancellationToken 的异步操作，超时后 token 会被取消</param>
    /// <param name="timeout">超时阈值，应小于 UI 看门狗的 45 秒</param>
    /// <param name="errorTitle">错误弹窗标题</param>
    protected async Task<bool> SafeExecuteAsync(Func<CancellationToken, Task> action, TimeSpan timeout, string? errorTitle = null)
    {
        errorTitle ??= Resources.Common_OperationFailed;
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await action(cts.Token);
            return true;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Logger?.LogWarning("操作超时：{Title}（{Seconds} 秒）", errorTitle, timeout.TotalSeconds);
            await Dialog.ShowErrorAsync(Resources.Common_OperationTimeout,
                string.Format(Resources.Common_TimeoutFormat, errorTitle, timeout.TotalSeconds));
            return false;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "ViewModel 操作失败：{Title}", errorTitle);
            await Dialog.ShowErrorAsync(errorTitle, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 导航离开前调用。子类可重写以询问用户是否保存未提交的更改。
    /// 返回 true 表示允许离开，false 表示取消导航。
    /// </summary>
    public virtual Task<bool> CanLeaveAsync() => Task.FromResult(true);
}
