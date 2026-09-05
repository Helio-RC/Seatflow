using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Avalonia.Services.Web;

/// <summary>
/// 浏览器端（WASM）占位对话框服务（Phase 2 将替换为 overlay 实现）。
/// 所有操作静默返回默认值，保证 Browser DI 可正常解析。
/// </summary>
public sealed class WebStubDialogService : IDialogService
{
    public void SetTopLevel (TopLevel topLevel) { }

    public Task ShowErrorAsync (string title , string message) => Task.CompletedTask;

    public Task ShowWarningAsync (string title , string message) => Task.CompletedTask;

    public Task ShowInfoAsync (string title , string message) => Task.CompletedTask;

    public Task<bool> ShowConfirmAsync (string title , string message) => Task.FromResult(false);

    public Task<(bool Confirmed , string Input)> ShowInputAsync (string title , string prompt , string initialValue = "")
        => Task.FromResult((false , initialValue));

    public Task<int?> ShowMultiOptionAsync (string title , string message ,
        string primaryText , string secondaryText , string? cancelText = null)
        => Task.FromResult<int?>(null);
}
