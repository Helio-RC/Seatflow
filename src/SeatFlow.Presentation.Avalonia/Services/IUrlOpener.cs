namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 在系统默认浏览器中打开 URL（桌面 = Process.Start；
/// 浏览器端（WASM）= window.open JS interop）。
/// </summary>
public interface IUrlOpener
{
    /// <summary>打开指定网址。</summary>
    void OpenUrl (string url);
}
