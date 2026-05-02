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
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    private bool _isLoading;

    public bool IsNotLoading => !IsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData))]
    private bool _isEmpty = true;

    public bool HasData => !IsEmpty;

    [ObservableProperty]
    private string _statusMessage = "就绪，请导入学生数据";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private int _studentCount;

    [ObservableProperty]
    private ObservableCollection<StudentDatasetInfo> _savedDatasets = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDataset))]
    private StudentDatasetInfo? _selectedDataset;

    [ObservableProperty]
    private bool _isLoadingDatasets;

    [ObservableProperty]
    private string? _currentDatasetId;

    [ObservableProperty]
    private string? _currentDatasetName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExpanded))]
    private bool _isCompact;

    public bool HasExpanded => !IsCompact;
    public bool HasSelectedDataset => SelectedDataset is not null;

    public DataManagementViewModel (IApplicationFacade facade , IFileService fileService , IDialogService dialog)
    {
        _facade = facade;
        _fileService = fileService;
        _dialog = dialog;
        _ = RefreshDatasetsAsync(CancellationToken.None);
    }

    private async Task RefreshDatasetsAsync (CancellationToken ct)
    {
        try
        {
            var datasets = await _facade.ListStudentDatasetsAsync(ct);
            SavedDatasets = new ObservableCollection<StudentDatasetInfo>(datasets);
        }
        catch
        {
            // 静默处理
        }
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

    private static readonly Dictionary<string , (string Suffix , string DisplayName)> TemplateLocales = new()
    {
        ["zh_cn"] = ("zh_cn" , "学生导入模板.xlsx") ,
        ["zh_tw"] = ("zh_tw" , "學生匯入範本.xlsx") ,
        ["ja_jp"] = ("ja_jp" , "学生インポートテンプレート.xlsx") ,
        ["ko_kr"] = ("ko_kr" , "학생가져오기템플릿.xlsx") ,
    };

    private const string DefaultTemplateSuffix = "en_us";
    private const string DefaultTemplateDisplayName = "StudentImportTemplate.xlsx";

    [RelayCommand]
    private async Task ExportTemplateAsync (CancellationToken ct)
    {
        string? errorTitle = null;
        string? errorMsg = null;

        try
        {
            var (suffix , displayName) = await ResolveTemplateLocaleAsync(ct);
            var uri = new Uri($"avares://A_Pair.Presentation.Avalonia/Assets/Files/Sample_{suffix}.xlsx");

            if (!AssetLoader.Exists(uri))
            {
                suffix = DefaultTemplateSuffix;
                displayName = DefaultTemplateDisplayName;
                uri = new Uri($"avares://A_Pair.Presentation.Avalonia/Assets/Files/Sample_{suffix}.xlsx");
            }

            if (!AssetLoader.Exists(uri))
            {
                errorTitle = "模板缺失";
                errorMsg = $"未找到内置模板文件 Sample_{suffix}.xlsx，请将模板放入 Assets/Files/ 目录。";
                return;
            }

            var file = await _fileService.SaveFileAsync("保存导入模板" , TemplateFileTypes , displayName);
            if (file is null) return;

            using var source = AssetLoader.Open(uri);
            await using var destination = File.Create(file.Path.LocalPath);
            await source.CopyToAsync(destination , ct);

            StatusMessage = "模板已保存";
        }
        catch (Exception ex)
        {
            errorTitle = "保存模板失败";
            errorMsg = $"无法保存模板文件。\n{ex.Message}";
        }
        finally
        {
            if (errorTitle != null)
                await _dialog.ShowErrorAsync(errorTitle , errorMsg!);
        }
    }

    private async Task<(string Suffix , string DisplayName)> ResolveTemplateLocaleAsync (CancellationToken ct)
    {
        try
        {
            var settings = await _facade.LoadAppSettingsAsync(ct);
            var lang = !string.IsNullOrEmpty(settings.Language)
                ? settings.Language
                : CultureInfo.CurrentUICulture.Name.Replace('-' , '_').ToLowerInvariant();

            if (TemplateLocales.TryGetValue(lang , out var entry))
                return entry;

            var prefix = lang.Split('_')[0];
            var fallback = TemplateLocales.FirstOrDefault(kv => kv.Key.StartsWith(prefix));
            return fallback.Value is (var f, var d) ? (f , d) : (DefaultTemplateSuffix , DefaultTemplateDisplayName);
        }
        catch
        {
            return (DefaultTemplateSuffix , DefaultTemplateDisplayName);
        }
    }

    [RelayCommand]
    private async Task ImportAsync (CancellationToken ct)
    {
        string? errorTitle = null;
        string? errorMsg = null;

        try
        {
            var file = await _fileService.OpenFileAsync("导入学生数据" , StudentFileTypes);
            if (file is null) return;

            FilePath = file.Path.LocalPath;
            IsLoading = true;
            ErrorMessage = string.Empty;
            StatusMessage = "正在导入...";

            var students = await _facade.LoadStudentsAsync(FilePath , ct);

            Students = new ObservableCollection<Student>(students);
            StudentCount = Students.Count;
            IsEmpty = StudentCount == 0;
            StatusMessage = IsEmpty ? "未导入任何学生数据" : $"已导入 {StudentCount} 名学生";

            // 自动保存到托管存储
            if (!IsEmpty)
            {
                var name = Path.GetFileNameWithoutExtension(FilePath);
                CurrentDatasetId = await _facade.SaveStudentDatasetAsync(name , students , Path.GetFileName(FilePath) , ct);
                CurrentDatasetName = name;
                _ = RefreshDatasetsAsync(ct);
            }

            if (IsEmpty)
            {
                errorTitle = "导入结果";
                errorMsg = "文件中没有读取到有效的学生数据，请检查文件格式是否正确。";
            }
        }
        catch (Exception ex)
        {
            errorTitle = "导入失败";
            errorMsg = ex is FileNotFoundException
                ? $"找不到文件：{FilePath}"
                : $"无法导入学生数据。\n{ex.Message}";
            StatusMessage = "导入失败";
        }
        finally
        {
            IsLoading = false;
            if (errorTitle != null)
                await _dialog.ShowErrorAsync(errorTitle , errorMsg!);
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync (CancellationToken ct)
    {
        await ExportAsync(ExportFormat.Csv , [new("CSV 文件") { Patterns = ["*.csv"] }] , ct);
    }

    [RelayCommand]
    private async Task ExportExcelAsync (CancellationToken ct)
    {
        await ExportAsync(ExportFormat.Excel , [new("Excel 文件") { Patterns = ["*.xlsx"] }] , ct);
    }

    [RelayCommand]
    private async Task ExportJsonAsync (CancellationToken ct)
    {
        await ExportAsync(ExportFormat.Json , [new("JSON 文件") { Patterns = ["*.json"] }] , ct);
    }

    private async Task ExportAsync (ExportFormat format , FilePickerFileType[] types , CancellationToken ct)
    {
        string? errorTitle = null;
        string? errorMsg = null;

        if (Students.Count == 0)
        {
            await _dialog.ShowWarningAsync("无数据" , "当前没有可导出的学生数据。");
            return;
        }

        try
        {
            var file = await _fileService.SaveFileAsync("导出" , types);
            if (file is null) return;

            IsLoading = true;
            ErrorMessage = string.Empty;
            StatusMessage = "正在导出...";

            await _facade.ExportStudentsAsync(file.Path.LocalPath , Students , format , ct);

            StatusMessage = "导出完成";
        }
        catch (Exception ex)
        {
            errorTitle = "导出失败";
            errorMsg = $"无法导出学生数据。\n{ex.Message}";
            StatusMessage = "导出失败";
        }
        finally
        {
            IsLoading = false;
            if (errorTitle != null)
                await _dialog.ShowErrorAsync(errorTitle , errorMsg!);
        }
    }

    [RelayCommand]
    private async Task ClearDataAsync ()
    {
        if (!IsEmpty)
        {
            var confirmed = await _dialog.ShowConfirmAsync("确认清除" ,
                $"确定要清除当前导入的 {StudentCount} 名学生数据吗？");
            if (!confirmed) return;
        }

        Students.Clear();
        StudentCount = 0;
        IsEmpty = true;
        CurrentDatasetId = null;
        CurrentDatasetName = null;
        FilePath = string.Empty;
        ErrorMessage = string.Empty;
        StatusMessage = "就绪，请导入学生数据";
    }

    [RelayCommand]
    private async Task LoadSelectedDatasetAsync (CancellationToken ct)
    {
        if (SelectedDataset is null) return;

        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = "正在加载...";

        try
        {
            var students = await _facade.LoadStudentDatasetAsync(SelectedDataset.Id , ct);
            if (students is not null)
            {
                CurrentDatasetId = SelectedDataset.Id;
                CurrentDatasetName = SelectedDataset.Name;
                Students = new ObservableCollection<Student>(students);
                StudentCount = Students.Count;
                IsEmpty = StudentCount == 0;
                FilePath = SelectedDataset.OriginalFileName ?? SelectedDataset.Name;
                StatusMessage = StudentCount > 0
                    ? $"已加载 {StudentCount} 名学生"
                    : "数据集为空";
            }
            else
            {
                StatusMessage = "数据集文件不存在";
                await _dialog.ShowErrorAsync("加载失败" , $"找不到数据集「{SelectedDataset.Name}」的文件。");
                await RefreshDatasetsAsync(ct);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "加载失败";
            await _dialog.ShowErrorAsync("加载失败" , ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedDatasetAsync (CancellationToken ct)
    {
        if (SelectedDataset is null) return;

        var confirmed = await _dialog.ShowConfirmAsync("确认删除" ,
            $"确定要删除数据集「{SelectedDataset.Name}」吗？\n此操作不可撤销。");
        if (!confirmed) return;

        try
        {
            await _facade.DeleteStudentDatasetAsync(SelectedDataset.Id , ct);
            SelectedDataset = null;
            await RefreshDatasetsAsync(ct);
            StatusMessage = "数据集已删除";
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync("删除失败" , ex.Message);
        }
    }

    [RelayCommand]
    private async Task RenameSelectedDatasetAsync (CancellationToken ct)
    {
        if (SelectedDataset is null) return;

        var (confirmed , newName) = await _dialog.ShowInputAsync("重命名数据集" ,
            $"请输入「{SelectedDataset.Name}」的新名称：" , SelectedDataset.Name);
        if (!confirmed || string.IsNullOrWhiteSpace(newName)) return;

        try
        {
            // 如果是当前数据集，用内存中的最新数据；否则从文件加载
            var students = CurrentDatasetId == SelectedDataset.Id
                ? Students.ToList()
                : await _facade.LoadStudentDatasetAsync(SelectedDataset.Id , ct);

            if (students is null) return;

            await _facade.DeleteStudentDatasetAsync(SelectedDataset.Id , ct);
            var newId = await _facade.SaveStudentDatasetAsync(newName.Trim() , students , SelectedDataset.OriginalFileName , ct);

            if (CurrentDatasetId == SelectedDataset.Id)
            {
                CurrentDatasetId = newId;
                CurrentDatasetName = newName.Trim();
            }

            SelectedDataset = null;
            await RefreshDatasetsAsync(ct);
            StatusMessage = "数据集已重命名";
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync("重命名失败" , ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveAsync (CancellationToken ct)
    {
        if (Students.Count == 0) return;

        var errors = ValidateStudents();
        if (errors.Count > 0)
        {
            await _dialog.ShowErrorAsync("数据校验未通过" ,
                string.Join('\n' , errors.Take(10)));
            return;
        }

        var datasetName = CurrentDatasetName ?? "未命名";

        var confirmed = await _dialog.ShowConfirmAsync("确认保存" ,
            $"将覆盖数据集「{datasetName}」（{StudentCount} 名学生），确定继续？");
        if (!confirmed) return;

        try
        {
            if (CurrentDatasetId is not null)
                await _facade.DeleteStudentDatasetAsync(CurrentDatasetId , ct);

            CurrentDatasetId = await _facade.SaveStudentDatasetAsync(datasetName , Students.ToList() , null , ct);
            CurrentDatasetName = datasetName;
            await RefreshDatasetsAsync(ct);
            StatusMessage = $"已保存「{datasetName}」";
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync("保存失败" , ex.Message);
        }
    }

    private List<string> ValidateStudents()
    {
        var errors = new List<string>();

        for (int i = 0; i < Students.Count; i++)
        {
            var s = Students[i];
            var row = i + 1;

            if (string.IsNullOrWhiteSpace(s.Name))
                errors.Add($"第 {row} 行：姓名不能为空");

            if (s.Height.HasValue && s.Height.Value <= 0)
                errors.Add($"第 {row} 行 \"{s.Name}\"：身高必须为正数");

            if (s.Gender.HasValue && !Enum.IsDefined(s.Gender.Value))
                errors.Add($"第 {row} 行 \"{s.Name}\"：性别值无效，请输入 男/女/其他 或 Male/Female/Other");
        }

        return errors;
    }



    [RelayCommand]
    private async Task RenameSaveAsync (CancellationToken ct)
    {
        var (confirmed , newName) = await _dialog.ShowInputAsync("另存为" ,
            "输入新数据集名称，原数据集不会被修改：" , "");
        if (!confirmed || string.IsNullOrWhiteSpace(newName)) return;

        try
        {
            var newId = await _facade.SaveStudentDatasetAsync(newName.Trim() , Students.ToList() , null , ct);
            CurrentDatasetId = newId;
            CurrentDatasetName = newName.Trim();
            await RefreshDatasetsAsync(ct);
            StatusMessage = $"已保存为「{newName.Trim()}」";
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync("保存失败" , ex.Message);
        }
    }
}
