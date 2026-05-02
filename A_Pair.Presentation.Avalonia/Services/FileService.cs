using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace A_Pair.Presentation.Avalonia.Services;

public class FileService : IFileService
{
    private TopLevel? _topLevel;

    public void SetTopLevel(TopLevel topLevel) => _topLevel = topLevel;

    public async Task<IStorageFile?> OpenFileAsync(string title, IReadOnlyList<FilePickerFileType> types)
    {
        if (_topLevel is null) return null;
        var files = await _topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            FileTypeFilter = types,
            AllowMultiple = false
        });
        return files.Count > 0 ? files[0] : null;
    }

    public async Task<IStorageFile?> SaveFileAsync(string title, IReadOnlyList<FilePickerFileType> types, string? suggestedFileName = null)
    {
        if (_topLevel is null) return null;
        return await _topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            FileTypeChoices = types,
            SuggestedFileName = suggestedFileName
        });
    }
}
