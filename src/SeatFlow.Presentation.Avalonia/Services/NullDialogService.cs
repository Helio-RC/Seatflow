using System.Threading.Tasks;
using Avalonia.Controls;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 空对象对话框服务：用于无法获得真实 <see cref="IDialogService"/> 的场景
/// （如代码中直接 new 的辅助 ViewModel / 设计时 / 单元测试）。
/// 所有方法静默返回默认值，不弹窗。
/// </summary>
public sealed class NullDialogService : IDialogService
{
    public static readonly NullDialogService Instance = new();

    private NullDialogService() { }

    public void SetTopLevel(TopLevel topLevel) { }

    public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;

    public Task ShowWarningAsync(string title, string message) => Task.CompletedTask;

    public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;

    public Task<bool> ShowConfirmAsync(string title, string message) => Task.FromResult(false);

    public Task<(bool Confirmed, string Input)> ShowInputAsync(string title, string prompt, string initialValue = "")
        => Task.FromResult((false, string.Empty));

    public Task<int?> ShowMultiOptionAsync(string title, string message,
        string primaryText, string secondaryText, string? cancelText = null)
        => Task.FromResult<int?>(null);
}
