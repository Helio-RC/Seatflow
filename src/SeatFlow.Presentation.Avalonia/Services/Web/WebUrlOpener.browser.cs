#if BROWSER
using System.Runtime.InteropServices.JavaScript;

namespace SeatFlow.Presentation.Avalonia.Services.Web;

/// <summary>
/// 浏览器端（WASM）URL 打开器：<c>window.open</c> JS interop。
/// </summary>
public partial class WebUrlOpener : IUrlOpener
{
    /// <summary>JS 全局 open 函数（window.open），新标签页打开。</summary>
    [JSImport("globalThis.open")]
    internal static partial void OpenRaw (string url);

    public void OpenUrl (string url) => OpenRaw(url);
}
#endif
