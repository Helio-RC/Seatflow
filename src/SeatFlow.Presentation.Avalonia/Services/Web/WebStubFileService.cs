using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Avalonia.Services.Web;

/// <summary>
/// 浏览器端（WASM）占位文件服务（Phase 2 将替换为 JS interop 实现）。
/// </summary>
public sealed class WebStubFileService : IFileService
{
    public void SetTopLevel (TopLevel topLevel) { }

    public Task<IStorageFile?> OpenFileAsync (string title , IReadOnlyList<FilePickerFileType> types)
        => Task.FromResult<IStorageFile?>(null);

    public Task<IStorageFile?> SaveFileAsync (string title , IReadOnlyList<FilePickerFileType> types , string? suggestedFileName = null)
        => Task.FromResult<IStorageFile?>(null);
}
