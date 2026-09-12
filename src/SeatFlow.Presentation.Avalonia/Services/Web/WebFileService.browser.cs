#if BROWSER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Avalonia.Services.Web;

/// <summary>
/// 浏览器端（WASM）文件服务：
/// 打开 = <c>&lt;input type=file&gt;</c> 的 ArrayBuffer；保存 = Blob + <c>&lt;a download&gt;</c>。
/// 字节以 base64 为 interop 载体（源生成 JSImport 的 Task 泛型限制）。
/// </summary>
[SupportedOSPlatform("browser")]
public partial class WebFileService : IFileService
{
    [JSImport("sfPickFile" , "sf.files")]
    internal static partial Task<string?> PickFile (string accept);

    [JSImport("sfDownloadFile" , "sf.files")]
    internal static partial Task DownloadFile (string fileName , string base64Content);

    public void SetTopLevel (TopLevel topLevel) { }

    public Task<IStorageFile?> OpenFileAsync (string title , IReadOnlyList<FilePickerFileType> types)
        => Task.FromResult<IStorageFile?>(null);

    public Task<IStorageFile?> SaveFileAsync (string title , IReadOnlyList<FilePickerFileType> types , string? suggestedFileName = null)
        => Task.FromResult<IStorageFile?>(null);

    public async Task<PickedFile?> OpenFileBytesAsync (string title , IReadOnlyList<FilePickerFileType> types)
    {
        var accept = ToAcceptString(types);
        var json = await PickFile(accept);
        if (string.IsNullOrEmpty(json)) return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var name = root.GetProperty("name").GetString() ?? "";
        var b64 = root.GetProperty("b64").GetString() ?? "";
        if (b64.Length == 0) return null;

        return new PickedFile(name , Convert.FromBase64String(b64));
    }

    public Task SaveFileBytesAsync (string suggestedFileName , byte[] content , IReadOnlyList<FilePickerFileType> types)
        => DownloadFile(suggestedFileName , Convert.ToBase64String(content));

    private static string ToAcceptString (IReadOnlyList<FilePickerFileType> types)
        => string.Join("," , types.SelectMany(t => t.Patterns ?? []));
}
#endif
