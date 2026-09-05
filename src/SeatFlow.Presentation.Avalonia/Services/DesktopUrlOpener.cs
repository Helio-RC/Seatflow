using System.Diagnostics;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 桌面端 URL 打开器：调用系统 <c>Process.Start</c>（ShellExecute）。
/// </summary>
public sealed class DesktopUrlOpener : IUrlOpener
{
    public void OpenUrl (string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // 打开失败静默：调用方已有兜底
        }
    }
}
