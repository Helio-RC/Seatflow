using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Presentation.Avalonia.Services;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class DataManagementViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly IFileService _fileService;

    [ObservableProperty]
    private ObservableCollection<Student> _students = [];

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _statusMessage = "就绪，请导入学生数据";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private int _studentCount;

    [ObservableProperty]
    private int _selectedIndex = -1;

    public DataManagementViewModel(IApplicationFacade facade, IFileService fileService)
    {
        _facade = facade;
        _fileService = fileService;
    }

    private static readonly FilePickerFileType[] StudentFileTypes =
    [
        new("学生数据文件") { Patterns = ["*.csv", "*.xlsx", "*.json"] },
        new("CSV 文件") { Patterns = ["*.csv"] },
        new("Excel 文件") { Patterns = ["*.xlsx"] },
        new("JSON 文件") { Patterns = ["*.json"] },
        FilePickerFileTypes.All
    ];

    private static readonly FilePickerFileType[] TemplateFileTypes =
    [
        new("Excel 文件") { Patterns = ["*.xlsx"] }
    ];

    private static readonly Dictionary<string, (string FileName, string DisplayName)> TemplateLocales = new()
    {
        ["zh_cn"] = ("Sample_zh_cn.xlsx", "学生导入模板.xlsx"),
        ["zh_tw"] = ("Sample_zh_tw.xlsx", "學生匯入範本.xlsx"),
        ["ja_jp"] = ("Sample_ja_jp.xlsx", "学生インポートテンプレート.xlsx"),
        ["ko_kr"] = ("Sample_ko_kr.xlsx", "학생가져오기템플릿.xlsx"),
    };

    private const string DefaultTemplateSuffix = "en_us";
    private const string DefaultTemplateDisplayName = "StudentImportTemplate.xlsx";

    [RelayCommand]
    private async Task ExportTemplateAsync(CancellationToken cancellationToken)
    {
        var (suffix, displayName) = await ResolveTemplateLocaleAsync(cancellationToken);
        var uri = new Uri($"avares://A_Pair.Presentation.Avalonia/Assets/Files/Sample_{suffix}.xlsx");

        // 如果语言对应的模板不存在，回退到 en_us
        if (!AssetLoader.Exists(uri))
        {
            suffix = DefaultTemplateSuffix;
            displayName = DefaultTemplateDisplayName;
            uri = new Uri($"avares://A_Pair.Presentation.Avalonia/Assets/Files/Sample_{suffix}.xlsx");
        }

        var file = await _fileService.SaveFileAsync("保存导入模板", TemplateFileTypes, displayName);
        if (file is null) return;

        using var source = AssetLoader.Open(uri);
        await using var destination = File.Create(file.Path.LocalPath);
        await source.CopyToAsync(destination, cancellationToken);

        StatusMessage = "模板已保存";
    }

    private async Task<(string Suffix, string DisplayName)> ResolveTemplateLocaleAsync(CancellationToken ct)
    {
        var settings = await _facade.LoadAppSettingsAsync(ct);
        var lang = !string.IsNullOrEmpty(settings.Language)
            ? settings.Language
            : CultureInfo.CurrentUICulture.Name.Replace('-', '_').ToLowerInvariant();

        if (TemplateLocales.TryGetValue(lang, out var entry))
            return entry;

        // 尝试只匹配语言部分（如 zh 匹配 zh_cn）
        var prefix = lang.Split('_')[0];
        var fallback = TemplateLocales.FirstOrDefault(kv => kv.Key.StartsWith(prefix));
        return fallback.Value is (var f, var d) ? (f, d) : (DefaultTemplateSuffix, DefaultTemplateDisplayName);
    }

    [RelayCommand]
    private async Task ImportAsync(CancellationToken cancellationToken)
    {
        var file = await _fileService.OpenFileAsync("导入学生数据", StudentFileTypes);
        if (file is null) return;

        FilePath = file.Path.LocalPath;
        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = "正在导入...";

        var students = await _facade.LoadStudentsAsync(FilePath, cancellationToken);

        Students = new ObservableCollection<Student>(students);
        StudentCount = Students.Count;
        IsEmpty = StudentCount == 0;
        IsLoading = false;
        StatusMessage = IsEmpty ? "未导入任何学生数据" : $"已导入 {StudentCount} 名学生";
    }

    [RelayCommand]
    private async Task ExportCsvAsync(CancellationToken cancellationToken)
    {
        await ExportAsync(ExportFormat.Csv, "导出 CSV", [new("CSV 文件") { Patterns = ["*.csv"] }], cancellationToken);
    }

    [RelayCommand]
    private async Task ExportExcelAsync(CancellationToken cancellationToken)
    {
        await ExportAsync(ExportFormat.Excel, "导出 Excel", [new("Excel 文件") { Patterns = ["*.xlsx"] }], cancellationToken);
    }

    [RelayCommand]
    private async Task ExportJsonAsync(CancellationToken cancellationToken)
    {
        await ExportAsync(ExportFormat.Json, "导出 JSON", [new("JSON 文件") { Patterns = ["*.json"] }], cancellationToken);
    }

    private async Task ExportAsync(ExportFormat format, string title, FilePickerFileType[] types, CancellationToken ct)
    {
        if (Students.Count == 0)
        {
            ErrorMessage = "没有可导出的数据";
            return;
        }

        var file = await _fileService.SaveFileAsync(title, types);
        if (file is null) return;

        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = "正在导出...";

        await _facade.ExportStudentsAsync(file.Path.LocalPath, Students, format, ct);

        IsLoading = false;
        StatusMessage = "导出完成";
    }

    [RelayCommand]
    private void ClearData()
    {
        Students.Clear();
        StudentCount = 0;
        IsEmpty = true;
        FilePath = string.Empty;
        ErrorMessage = string.Empty;
        StatusMessage = "就绪，请导入学生数据";
    }
}
