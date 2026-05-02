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
    private readonly IDialogService _dialog;

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

    public DataManagementViewModel(IApplicationFacade facade, IFileService fileService, IDialogService dialog)
    {
        _facade = facade;
        _fileService = fileService;
        _dialog = dialog;
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

    private static readonly Dictionary<string, (string Suffix, string DisplayName)> TemplateLocales = new()
    {
        ["zh_cn"] = ("zh_cn", "学生导入模板.xlsx"),
        ["zh_tw"] = ("zh_tw", "學生匯入範本.xlsx"),
        ["ja_jp"] = ("ja_jp", "学生インポートテンプレート.xlsx"),
        ["ko_kr"] = ("ko_kr", "학생가져오기템플릿.xlsx"),
    };

    private const string DefaultTemplateSuffix = "en_us";
    private const string DefaultTemplateDisplayName = "StudentImportTemplate.xlsx";

    [RelayCommand]
    private async Task ExportTemplateAsync(CancellationToken ct)
    {
        try
        {
            var (suffix, displayName) = await ResolveTemplateLocaleAsync(ct);
            var uri = new Uri($"avares://A_Pair.Presentation.Avalonia/Assets/Files/Sample_{suffix}.xlsx");

            if (!AssetLoader.Exists(uri))
            {
                suffix = DefaultTemplateSuffix;
                displayName = DefaultTemplateDisplayName;
                uri = new Uri($"avares://A_Pair.Presentation.Avalonia/Assets/Files/Sample_{suffix}.xlsx");
            }

            if (!AssetLoader.Exists(uri))
            {
                await _dialog.ShowErrorAsync("模板缺失", $"未找到内置模板文件 Sample_{suffix}.xlsx，请将模板放入 Assets/Files/ 目录。");
                return;
            }

            var file = await _fileService.SaveFileAsync("保存导入模板", TemplateFileTypes, displayName);
            if (file is null) return;

            using var source = AssetLoader.Open(uri);
            await using var destination = File.Create(file.Path.LocalPath);
            await source.CopyToAsync(destination, ct);

            StatusMessage = "模板已保存";
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync("保存模板失败", $"无法保存模板文件。\n{ex.Message}");
        }
    }

    private async Task<(string Suffix, string DisplayName)> ResolveTemplateLocaleAsync(CancellationToken ct)
    {
        try
        {
            var settings = await _facade.LoadAppSettingsAsync(ct);
            var lang = !string.IsNullOrEmpty(settings.Language)
                ? settings.Language
                : CultureInfo.CurrentUICulture.Name.Replace('-', '_').ToLowerInvariant();

            if (TemplateLocales.TryGetValue(lang, out var entry))
                return entry;

            var prefix = lang.Split('_')[0];
            var fallback = TemplateLocales.FirstOrDefault(kv => kv.Key.StartsWith(prefix));
            return fallback.Value is (var f, var d) ? (f, d) : (DefaultTemplateSuffix, DefaultTemplateDisplayName);
        }
        catch
        {
            return (DefaultTemplateSuffix, DefaultTemplateDisplayName);
        }
    }

    [RelayCommand]
    private async Task ImportAsync(CancellationToken ct)
    {
        try
        {
            var file = await _fileService.OpenFileAsync("导入学生数据", StudentFileTypes);
            if (file is null) return;

            FilePath = file.Path.LocalPath;
            IsLoading = true;
            ErrorMessage = string.Empty;
            StatusMessage = "正在导入...";

            var students = await _facade.LoadStudentsAsync(FilePath, ct);

            Students = new ObservableCollection<Student>(students);
            StudentCount = Students.Count;
            IsEmpty = StudentCount == 0;
            IsLoading = false;
            StatusMessage = IsEmpty ? "未导入任何学生数据" : $"已导入 {StudentCount} 名学生";

            if (IsEmpty)
                await _dialog.ShowWarningAsync("导入结果", "文件中没有读取到有效的学生数据，请检查文件格式是否正确。");
        }
        catch (FileNotFoundException)
        {
            await _dialog.ShowErrorAsync("文件不存在", $"找不到文件：{FilePath}");
            IsLoading = false;
            StatusMessage = "导入失败：文件不存在";
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync("导入失败", $"无法导入学生数据。\n{ex.Message}");
            IsLoading = false;
            StatusMessage = "导入失败";
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync(CancellationToken ct)
    {
        await ExportAsync(ExportFormat.Csv, "导出 CSV", [new("CSV 文件") { Patterns = ["*.csv"] }], ct);
    }

    [RelayCommand]
    private async Task ExportExcelAsync(CancellationToken ct)
    {
        await ExportAsync(ExportFormat.Excel, "导出 Excel", [new("Excel 文件") { Patterns = ["*.xlsx"] }], ct);
    }

    [RelayCommand]
    private async Task ExportJsonAsync(CancellationToken ct)
    {
        await ExportAsync(ExportFormat.Json, "导出 JSON", [new("JSON 文件") { Patterns = ["*.json"] }], ct);
    }

    private async Task ExportAsync(ExportFormat format, string title, FilePickerFileType[] types, CancellationToken ct)
    {
        if (Students.Count == 0)
        {
            await _dialog.ShowWarningAsync("无数据", "当前没有可导出的学生数据。");
            return;
        }

        try
        {
            var file = await _fileService.SaveFileAsync(title, types);
            if (file is null) return;

            IsLoading = true;
            ErrorMessage = string.Empty;
            StatusMessage = "正在导出...";

            await _facade.ExportStudentsAsync(file.Path.LocalPath, Students, format, ct);

            IsLoading = false;
            StatusMessage = "导出完成";
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync("导出失败", $"无法导出学生数据。\n{ex.Message}");
            IsLoading = false;
            StatusMessage = "导出失败";
        }
    }

    [RelayCommand]
    private async Task ClearDataAsync()
    {
        if (!IsEmpty)
        {
            var confirmed = await _dialog.ShowConfirmAsync("确认清除",
                $"确定要清除当前导入的 {StudentCount} 名学生数据吗？");
            if (!confirmed) return;
        }

        Students.Clear();
        StudentCount = 0;
        IsEmpty = true;
        FilePath = string.Empty;
        ErrorMessage = string.Empty;
        StatusMessage = "就绪，请导入学生数据";
    }
}
