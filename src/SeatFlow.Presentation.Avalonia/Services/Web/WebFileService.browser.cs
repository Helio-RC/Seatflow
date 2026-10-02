#if BROWSER
using System;
using System.Collections.Generic;
using System.IO;
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

    /// <summary>
    /// 浏览器端打开文件：选择后把字节写入内存文件系统（Emscripten MEMFS）临时目录并返回其路径，
    /// 使现有按路径读取的导入管线（CSV/XLSX/JSON 等）在 Web 端等价可用；
    /// 保留原文件名以驱动扩展名 Provider 选择。
    /// </summary>
    public async Task<string?> OpenFilePathAsync (string title , IReadOnlyList<FilePickerFileType> types)
    {
        var picked = await OpenFileBytesAsync(title , types);
        if (picked is null) return null;

        var dir = Path.Combine(Path.GetTempPath() , "seatflow-uploads" , Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var fileName = Path.GetFileName(picked.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "upload.bin";
        var path = Path.Combine(dir , fileName);
        await File.WriteAllBytesAsync(path , picked.Content);
        return path;
    }

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
