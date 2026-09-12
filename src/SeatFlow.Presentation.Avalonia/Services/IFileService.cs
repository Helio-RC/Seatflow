using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>文件选择结果（跨平台一致的字节级载体）。</summary>
public sealed record PickedFile(string FileName, byte[] Content);

public interface IFileService
{
    void SetTopLevel(TopLevel topLevel);

    /// <summary>桌面专用：打开文件（返回 StorageProvider 句柄）。浏览器端返回 null。</summary>
    Task<IStorageFile?> OpenFileAsync(string title, IReadOnlyList<FilePickerFileType> types);

    /// <summary>桌面专用：保存文件（返回 StorageProvider 句柄）。浏览器端返回 null。</summary>
    Task<IStorageFile?> SaveFileAsync(string title, IReadOnlyList<FilePickerFileType> types, string? suggestedFileName = null);

    /// <summary>
    /// 打开文件并读取字节（跨平台：桌面 = StorageProvider 流读取，浏览器 = file input 的 ArrayBuffer）。
    /// </summary>
    Task<PickedFile?> OpenFileBytesAsync(string title, IReadOnlyList<FilePickerFileType> types);

    /// <summary>
    /// 保存字节到文件（跨平台：桌面 = StorageProvider 写入，浏览器 = Blob 下载）。
    /// </summary>
    Task SaveFileBytesAsync(string suggestedFileName, byte[] content, IReadOnlyList<FilePickerFileType> types);
}
