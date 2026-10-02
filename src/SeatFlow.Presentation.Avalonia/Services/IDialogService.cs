using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using SeatFlow.Core.Models.SeatSets;

namespace SeatFlow.Presentation.Avalonia.Services;

public interface IDialogService
{
    void SetTopLevel(TopLevel topLevel);
    Task ShowErrorAsync(string title, string message);
    Task ShowWarningAsync(string title, string message);
    Task ShowInfoAsync(string title, string message);
    Task<bool> ShowConfirmAsync(string title, string message);

    /// <summary>显示文本输入对话框，返回 (是否确认, 输入文本)。</summary>
    Task<(bool Confirmed, string Input)> ShowInputAsync(string title, string prompt, string initialValue = "");

    /// <summary>显示多按钮对话框，返回 null(Windw关闭) / 0(第一个按钮) / 1(第二个按钮) / 2(第三个按钮)。</summary>
    Task<int?> ShowMultiOptionAsync(string title, string message,
        string primaryText, string secondaryText, string? cancelText = null);

    /// <summary>
    /// 显示 .seatsets 数据类别选择对话框（桌面 = 独立窗口；浏览器 = overlay），
    /// 返回用户选择；取消返回 <c>null</c>。
    /// </summary>
    /// <param name="isExport">true = 导出模式；false = 导入模式。</param>
    /// <param name="available">导入模式下可用的类别（用于预填复选框）；导出模式传 null。</param>
    Task<SeatSetsExportSelection?> ShowSeatSetsSelectionAsync(
        bool isExport, SeatSetsExportSelection? available = null, CancellationToken ct = default);
}
