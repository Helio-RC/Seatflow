using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Enums;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Helpers;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 人员名单页（M4）：
/// - 表格恢复 ListBox 虚拟化（去 ScrollViewer 包裹），行使用「显示/编辑」轻量模板；
/// - 统一脏检查（<see cref="DirtyTracker"/>）与对话框门（<see cref="IDialogGate"/>，替代 _dialogLock + Task.Delay）；
/// - 生命周期 <see cref="IPageLifecycle"/>（构造器不再 fire-and-forget），保留 InitializationTask 供引导等待；
/// - 紧凑模式：数据集列表转为右侧抽屉（<see cref="SideDrawerState"/>）。
/// </summary>
public partial class MemberManagementViewModel : ViewModelBase, IPageLifecycle, IFileDropHandler, IGuideSeedTarget
{
    private readonly IApplicationFacade _facade;
    private readonly IFileService _fileService;
    private readonly IDialogService _dialog;
    private readonly IUrlOpener _urlOpener;
    private readonly IDialogGate _dialogGate;
    private readonly IShellLayoutService _layout;
    private readonly ILogger<MemberManagementViewModel> _logger;

    [ObservableProperty]
    public partial ObservableCollection<StudentRowViewModel> Students { get; set; } = [];

    /// <summary>
    /// 是否启用虚拟化（M4）：WASM 软件渲染下虚拟化按滚动分批实例化行的成本高于一次性
    /// 实例化（240 行实测每滚一次 300–400ms）；≤300 行改用非虚拟化 StackPanel 换取平滑滚动，
    /// 超过 300 行再启用虚拟化控制内存/首帧。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseStaticRoster))]
    public partial bool UseVirtualization { get; set; }

    /// <summary>非虚拟化模式（≤300 行）。</summary>
    public bool UseStaticRoster => !UseVirtualization;

    /// <summary>底部新增行的绑定源，用户填写后通过 AddNewStudentCommand 加入表格。</summary>
    [ObservableProperty]
    public partial Student NewStudent { get; set; } = new();

    [ObservableProperty]
    public partial string FilePath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    public partial bool IsLoading { get; set; }

    public bool IsNotLoading => !IsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData))]
    [NotifyPropertyChangedFor(nameof(IsImportMode))]
    [NotifyPropertyChangedFor(nameof(IsUpdateMode))]
    public partial bool IsEmpty { get; set; } = true;

    public bool HasData => !IsEmpty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = Resources.Member_Ready;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int StudentCount { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<StudentDatasetInfo> SavedDatasets { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDataset))]
    public partial StudentDatasetInfo? SelectedDataset { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingDatasets { get; set; }

    [ObservableProperty]
    public partial string? CurrentDatasetId { get; set; }

    [ObservableProperty]
    public partial string? CurrentDatasetName { get; set; }

    // ── M4 外壳：紧凑数据集抽屉 ──

    /// <summary>数据集列表面板状态（桌面内联 / 紧凑右抽屉）。</summary>
    public SideDrawerState DatasetsPanel { get; }

    /// <summary>外壳布局状态（紧凑断点）。</summary>
    public IShellLayoutService Layout => _layout;

    /// <summary>紧凑模式隐藏命令栏文字（避免溢出）。</summary>
    public bool CompactCommandTextVisible => !_layout.IsCompact;

    // ── 脏状态追踪（统一 DirtyTracker） ──
    private static readonly JsonSerializerOptions _studentJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly DirtyTracker _dirty = new();
    private StudentDatasetInfo? _previousDataset;
    private bool _suppressDatasetLoad;
    private bool _datasetsLoaded;

    // ── M5 引导演示数据（下沉自 OnboardingService） ──
    /// <summary>演示数据集的固定 ID，用于注入和清理时识别。</summary>
    private const string GuideDemoDatasetId = "guide-demo-ds";
    private bool _guideDemoInjected;
    /// <summary>引导注入前的用户状态（null 表示首次使用无需恢复）。</summary>
    private List<Student>? _guideSavedStudents;
    private List<StudentDatasetInfo>? _guideSavedDatasets;
    private bool _guideSavedIsEmpty;
    private CancellationTokenSource? _enterCts;
    /// <summary>本次进入页面加载完成的信号：OnEnter 完成置位；OnLeave 换新的未完成实例。</summary>
    private TaskCompletionSource _enterCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 本次进入页面初始化（数据集列表加载）的完成信号：
    /// OnLeave 换新、OnEnter 完成后置位，引导示例数据注入等待它。
    /// </summary>
    public Task InitializationTask => _enterCompletion.Task;

    private bool IsNewStudentDirty =>
        !string.IsNullOrWhiteSpace(NewStudent.Name) ||
        NewStudent.Height.HasValue ||
        NewStudent.Gender.HasValue ||
        NewStudent.NeedsFrontRow;

    /// <summary>是否存在未保存的更改（供生命周期与导航拦截使用）。</summary>
    public bool IsDirty => IsNewStudentDirty || _dirty.IsDirty;

    /// <summary>脏检查器（UI 可绑定 <c>Dirty.IsDirty</c> 显示状态徽标）。</summary>
    public DirtyTracker Dirty => _dirty;

    partial void OnStudentCountChanged(int value)
        => OnPropertyChanged(nameof(HeaderSubtitle));

    /// <summary>命令栏副标题：数据集名 · 人数。</summary>
    public string HeaderSubtitle =>
        $"{SelectedDataset?.Name ?? Resources.Member_Title} · {string.Format(Resources.Member_PersonCountFmt, StudentCount)}";

    private string SerializeStudents() =>
        JsonSerializer.Serialize(Students.Select(r => r.Student), _studentJsonOptions);

    private void MarkClean() => _dirty.MarkClean(SerializeStudents());
    private void RefreshDirty() => _dirty.Update(SerializeStudents());

    partial void OnSelectedDatasetChanged(StudentDatasetInfo? value)
    {
        OnPropertyChanged(nameof(HeaderSubtitle));
        if (_suppressDatasetLoad)
            return;
        if (value is null)
        {
            // 用户取消选中（Toggle 模式再次点击已选项） → 卸载数据
            _ = ClearDataInternalAsync();
            return;
        }
        _ = SwitchToDatasetAsync(value);
    }

    private async Task SwitchToDatasetAsync(StudentDatasetInfo target)
    {
        // 门被其他模态占用：不排队、不弹窗，直接回退选中（保持当前已加载的数据集）
        if (_dialogGate.IsBusy)
        {
            RevertDatasetSelection();
            return;
        }

        var executed = await _dialogGate.RunAsync(async () =>
        {
            if (IsDirty)
            {
                var choice = await Dialog.ShowMultiOptionAsync(
                    Resources.Member_UnsavedChanges,
                    Resources.Member_UnsavedChangesMsg,
                    Resources.Common_Save,
                    Resources.Common_Discard,
                    Resources.Common_Cancel);

                switch (choice)
                {
                    case 0: // 保存
                        await SaveInternalAsync(CancellationToken.None);
                        break;
                    case 1: // 放弃
                        break;
                    default: // 取消或关闭窗口
                        return false;
                }
            }

            NewStudent = new Student();
            await LoadDatasetAsync(target, CancellationToken.None);
            _previousDataset = target;
            return true;
        });

        // 用户取消（或门在检查后被占用）：回退选中项
        if (executed != true)
            RevertDatasetSelection();
    }

    /// <summary>回退数据集选中项到上一次成功加载的数据集（不触发加载副作用）。</summary>
    private void RevertDatasetSelection()
    {
        _suppressDatasetLoad = true;
        try
        {
            // _previousDataset 可能已被删除/列表已刷新（引用不在当前列表中）→ 回退为清空选中
            SelectedDataset = _previousDataset is not null && SavedDatasets.Contains(_previousDataset)
                ? _previousDataset
                : null;
        }
        finally
        {
            _suppressDatasetLoad = false;
        }
    }

    public bool HasSelectedDataset => SelectedDataset is not null;

    /// <summary>编辑区无数据时显示"从文件导入"按钮。</summary>
    public bool IsImportMode => !HasData;

    /// <summary>编辑区有数据时显示"从文件更新"按钮。</summary>
    public bool IsUpdateMode => HasData;

    /// <summary>标记数据集列表失效（下次进入或显式刷新时重新加载）。供 .seatsets 导入等场景调用。</summary>
    public void InvalidateData() => _datasetsLoaded = false;

    // ═══════════════════════════════════════════════
    // IGuideSeedTarget（M5：引导演示注入/清理下沉到页面）
    // ═══════════════════════════════════════════════

    /// <summary>获取底层学生列表（不含编辑态包装）。</summary>
    private List<Student> GetStudents() => Students.Select(r => r.Student).ToList();

    /// <summary>注入演示班级：保存用户原状态；用户已有数据时不覆盖，仅补演示数据集。</summary>
    public void SeedGuideData()
    {
        // 保存用户原有状态，引导结束后恢复（始终执行，即使跳过注入）
        _guideSavedStudents = [.. GetStudents()];
        _guideSavedDatasets = [.. SavedDatasets];
        _guideSavedIsEmpty = IsEmpty;

        // 若用户已在 Phase 1 导入数据，不覆盖
        if (GetStudents().Count > 0) return;

        ReplaceStudents(
        [
            new() { Name = "Alice", Height = 165, Gender = Gender.Female },
            new() { Name = "Bob", Height = 175, Gender = Gender.Male, NeedsFrontRow = true },
            new() { Name = "Charlie", Height = 180, Gender = Gender.Male },
            new() { Name = "Diana", Height = 160, Gender = Gender.Female },
            new() { Name = "Eve", Height = 170, Gender = Gender.Female },
            new() { Name = "Frank", Height = 178, Gender = Gender.Male },
        ]);
        RefreshDirty();
        IsLoading = false;
        StatusMessage = string.Format(Resources.Member_LoadedFmt, StudentCount);

        // 追加演示数据集到现有列表（而非替换），避免覆盖用户真实数据集
        var demoDataset = new StudentDatasetInfo
        {
            Id = GuideDemoDatasetId,
            Name = "演示班级",
            StudentCount = 6,
            CreatedAt = DateTime.Now
        };
        // 仅在演示数据集不存在时才追加，防止重复
        if (!SavedDatasets.Any(d => d.Id == GuideDemoDatasetId))
            SavedDatasets.Add(demoDataset);
        CurrentDatasetId = GuideDemoDatasetId;
        CurrentDatasetName = "演示班级";

        // 安全选中演示数据集，不触发磁盘加载
        _suppressDatasetLoad = true;
        SelectedDataset = demoDataset;
        _suppressDatasetLoad = false;

        _guideDemoInjected = true;
    }

    /// <summary>清理演示数据：仅在实际注入过时恢复注入前状态，避免触碰用户当前编辑内容。</summary>
    public void ClearGuideData()
    {
        if (_guideDemoInjected)
        {
            // 使用 _suppressDatasetLoad 阻断 SelectedDataset 变化时的副作用，
            // 防止 SavedDatasets 替换触发 OnSelectedDatasetChanged → ClearDataInternalAsync 弹窗。
            _suppressDatasetLoad = true;
            try
            {
                if (_guideSavedStudents is not null)
                {
                    // 引导前有用户数据 → 恢复到原始状态（显式过滤残留的演示数据集）
                    ReplaceStudents(_guideSavedStudents);
                    IsEmpty = _guideSavedIsEmpty;
                    SavedDatasets = new ObservableCollection<StudentDatasetInfo>(
                        (_guideSavedDatasets ?? []).Where(d => d.Id != GuideDemoDatasetId));
                }
                else
                {
                    // 首次使用（引导前无数据）→ 清空演示数据（显式过滤残留的演示数据集）
                    ReplaceStudents([]);
                    SavedDatasets = new ObservableCollection<StudentDatasetInfo>(
                        SavedDatasets.Where(d => d.Id != GuideDemoDatasetId));
                }
                SelectedDataset = null;
                CurrentDatasetId = null;
                CurrentDatasetName = null;
                FilePath = string.Empty;
                _dirty.Reset(); // 重置 DirtyTracker 基线，防止后续 IsDirty 误判
            }
            finally
            {
                _suppressDatasetLoad = false;
            }
        }

        _guideDemoInjected = false;
        _guideSavedStudents = null;
        _guideSavedDatasets = null;
    }

    /// <summary>
    /// 人员表格显示排序：姓名自然序（汉字按拼音、数字按大小），同名回退 Id 保证稳定。
    /// 落盘顺序仍由仓储按 Id 规范化为哈希用，二者互不影响。
    /// </summary>
    internal static IEnumerable<Student> SortStudents(IEnumerable<Student> students)
        => students
            .OrderBy(s => s.Name, NaturalStringComparer.Instance)
            .ThenBy(s => s.Id, StringComparer.Ordinal);

    /// <summary>人员显示排序键：姓名自然序 + Id 兜底。</summary>
    internal static int CompareStudents(Student a, Student b)
    {
        int byName = NaturalStringComparer.Instance.Compare(a.Name, b.Name);
        return byName != 0 ? byName : string.CompareOrdinal(a.Id, b.Id);
    }

    /// <summary>用底层学生集合替换行集合（订阅行变更以驱动脏检查）。</summary>
    private void ReplaceStudents(IEnumerable<Student> students)
    {
        foreach (var row in Students)
            row.PropertyChanged -= OnRowPropertyChanged;

        var rows = SortStudents(students).Select(s => new StudentRowViewModel(s)).ToList();
        foreach (var row in rows)
            row.PropertyChanged += OnRowPropertyChanged;

        Students = new ObservableCollection<StudentRowViewModel>(rows);
        StudentCount = rows.Count;
        IsEmpty = rows.Count == 0;
        UseVirtualization = rows.Count > 300;
    }

    /// <summary>把行移动到与显示排序一致的位置（重命名后保持列表有序）。</summary>
    private void MoveToSortedPosition(StudentRowViewModel row)
    {
        int oldIndex = Students.IndexOf(row);
        if (oldIndex < 0) return;

        int insertIndex = 0;
        for (int i = 0; i < Students.Count; i++)
        {
            if (i == oldIndex) continue;
            if (CompareStudents(row.Student, Students[i].Student) < 0) break;
            insertIndex++;
        }

        if (insertIndex != oldIndex)
            Students.Move(oldIndex, insertIndex);
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudentRowViewModel.IsEditing)) return;
        RefreshDirty();
    }

    public string StudentCountDisplay => string.Format(Resources.Member_MemberCountFmt, StudentCount);
    public string FilePathDisplay => string.IsNullOrEmpty(FilePath) ? "" : string.Format(Resources.Member_DataSourceFmt, FilePath);
    public string StudentCountDisplay2 => string.Format(Resources.Member_PersonCountFmt, StudentCount);

    public MemberManagementViewModel(
        IApplicationFacade facade,
        IFileService fileService,
        IDialogService dialog,
        IUrlOpener urlOpener,
        IDialogGate dialogGate,
        IShellLayoutService layout,
        ILogger<MemberManagementViewModel>? logger = null) : base(dialog, logger)
    {
        _facade = facade;
        _fileService = fileService;
        _dialog = dialog;
        _urlOpener = urlOpener;
        _dialogGate = dialogGate;
        _layout = layout;
        _logger = logger ?? NullLogger<MemberManagementViewModel>.Instance;
        DatasetsPanel = new SideDrawerState(layout, 0, 240,
            desktopBorder: new global::Avalonia.Thickness(0, 0, 1, 0));
        _layout.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IShellLayoutService.IsCompact))
                OnPropertyChanged(nameof(CompactCommandTextVisible));
        };
    }

    // ═══════════════════════════════════════════════
    // IPageLifecycle（M4：构造器不再 fire-and-forget 加载）
    // ═══════════════════════════════════════════════

    public async Task OnEnterAsync(CancellationToken ct)
    {
        _enterCts?.Cancel();
        _enterCts?.Dispose();
        _enterCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        DatasetsPanel.IsOpen = false;

        var completion = _enterCompletion;
        try
        {
            if (!_datasetsLoaded)
            {
                // 仅在真正加载成功后标记，取消/失败时下次进入重试
                _datasetsLoaded = await RefreshDatasetsAsync(_enterCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // 离开页面取消，保持未加载状态以便下次重试
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    public Task OnLeaveAsync()
    {
        _enterCts?.Cancel();
        DatasetsPanel.IsOpen = false;
        // 为下一次进入准备新的完成信号
        _enterCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return Task.CompletedTask;
    }

    /// <summary>加载数据集列表；成功返回 true（取消时抛出以便上层保留未加载状态）。</summary>
    private async Task<bool> RefreshDatasetsAsync(CancellationToken ct)
    {
        try
        {
            var datasets = await _facade.ListStudentDatasetsAsync(ct);
            SavedDatasets = new ObservableCollection<StudentDatasetInfo>(datasets);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "刷新数据集列表失败");
            return false;
        }
    }

    /// <summary>显式刷新数据集列表（命令栏/抽屉刷新按钮）。</summary>
    [RelayCommand]
    private async Task RefreshDatasets()
    {
        _datasetsLoaded = await RefreshDatasetsAsync(CancellationToken.None);
    }

    /// <summary>后台刷新数据集列表（吞掉取消，避免未观测的 OCE）。</summary>
    private async Task RefreshDatasetsQuietAsync(CancellationToken ct)
    {
        try { await RefreshDatasetsAsync(ct); }
        catch (OperationCanceledException) { /* 导入/保存过程中被取消，忽略 */ }
    }

    private static readonly FilePickerFileType[] StudentFileTypes =
    [
        new(Resources.Member_MemberDataFile) { Patterns = ["*.csv", "*.xlsx", "*.json"] },
        new(Resources.Data_CSVFile) { Patterns = ["*.csv"] },
        new(Resources.Data_ExcelFile) { Patterns = ["*.xlsx"] },
        new(Resources.Data_JSONFile) { Patterns = ["*.json"] },
        FilePickerFileTypes.All
    ];

    private static readonly FilePickerFileType[] TemplateFileTypes =
    [
        new(Resources.Data_ExcelFile) { Patterns = ["*.xlsx"] }
    ];

    private static readonly Dictionary<string, (string Suffix, string DisplayName)> TemplateLocales = new()
    {
        ["zh_cn"] = ("zh_cn", Resources.Member_SampleFileCN),
        ["zh_tw"] = ("zh_tw", "學生匯入範本.xlsx"),
        ["ja_jp"] = ("ja_jp", "学生インポートテンプレート.xlsx"),
        ["ko_kr"] = ("ko_kr", "학생가져오기템플릿.xlsx"),
    };

    private const string DefaultTemplateSuffix = "en_us";
    private const string DefaultTemplateDisplayName = "MemberImportTemplate.xlsx";

    /// <summary>
    /// 内置模板资源 URI。资源以 AvaloniaResource 编译在本程序集（Presentation.Avalonia），
    /// 不能使用宿主的程序集名（桌面为 SeatFlow），否则 AssetLoader 解析失败。
    /// </summary>
    internal static Uri BuildTemplateUri(string suffix)
        => new($"avares://{typeof(MemberManagementViewModel).Assembly.GetName().Name}/Assets/Files/Sample_{suffix}.xlsx");

    [RelayCommand]
    private async Task ExportTemplateAsync(CancellationToken ct)
    {
        await _dialogGate.RunAsync(async () =>
        {
            string? errorTitle = null;
            string? errorMsg = null;

            try
            {
                var (suffix, displayName) = await ResolveTemplateLocaleAsync(ct);
                var uri = BuildTemplateUri(suffix);

                if (!AssetLoader.Exists(uri))
                {
                    suffix = DefaultTemplateSuffix;
                    displayName = DefaultTemplateDisplayName;
                    uri = BuildTemplateUri(suffix);
                }

                if (!AssetLoader.Exists(uri))
                {
                    errorTitle = Resources.Member_TemplateMissing;
                    errorMsg = string.Format(Resources.Member_TemplateMissingMsg);
                    return;
                }

                if (OperatingSystem.IsBrowser())
                {
                    // WASM：嵌入资源 → 字节 → 浏览器下载
                    using var sourceMs = new MemoryStream();
                    await using (var src = AssetLoader.Open(uri))
                        await src.CopyToAsync(sourceMs, ct);
                    await _fileService.SaveFileBytesAsync(displayName, sourceMs.ToArray(), TemplateFileTypes);
                    StatusMessage = Resources.Data_TemplateSaved;
                }
                else
                {
                    IStorageFile? tmplFile;
                    try { tmplFile = await _fileService.SaveFileAsync(Resources.Common_Save, TemplateFileTypes, displayName); }
                    catch (Exception ex) { _logger.LogDebug(ex, "文件对话框取消或异常: 导出模板"); return; }
                    if (tmplFile is null) return;

                    using var source = AssetLoader.Open(uri);
                    await using var destination = File.Create(tmplFile.Path.LocalPath);
                    await source.CopyToAsync(destination, ct);

                    StatusMessage = Resources.Data_TemplateSaved;
                }
            }
            catch (Exception ex)
            {
                errorTitle = Resources.Data_TemplateSaveFailed;
                errorMsg = string.Format(Resources.Member_TemplateSaveError) + "\n" + ex.Message;
            }
            finally
            {
                if (errorTitle != null)
                    await _dialog.ShowErrorAsync(errorTitle, errorMsg!);
            }
        });
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
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "解析模板语言失败，使用默认值");
            return (DefaultTemplateSuffix, DefaultTemplateDisplayName);
        }
    }

    [RelayCommand]
    private async Task ImportAsync(CancellationToken ct)
    {
        await _dialogGate.RunAsync(async () =>
        {
            string? importPath;
            try { importPath = await _fileService.OpenFilePathAsync(Resources.Member_ImportData, StudentFileTypes); }
            catch (Exception ex) { _logger.LogDebug(ex, "文件对话框取消或异常: 导入"); return; }
            if (importPath is null) return;

            await ImportFromPathAsync(importPath, ct);
        });
    }

    /// <summary>70×70 自动扫描阈值。</summary>
    private const int MaxAutoScanSize = 70;

    /// <summary>打开用户文档中的人员管理导入帮助页面。</summary>
    private void OpenHelpDocs()
    {
        try
        {
            _urlOpener.OpenUrl("https://seatflow.work/docs/user/03-member-management#section-7");
        }
        catch
        {
            // 无法打开浏览器时静默忽略
        }
    }

    /// <summary>从指定路径导入学生数据（跳过文件对话框）。供 ImportAsync 和拖放使用。</summary>
    /// <returns>true 表示导入成功（至少有一条有效数据）。</returns>
    private async Task<bool> ImportFromPathAsync(string filePath, CancellationToken ct)
    {
        string? errorTitle = null;
        string? errorMsg = null;

        try
        {
            FilePath = filePath;
            IsLoading = true;
            ErrorMessage = string.Empty;
            StatusMessage = Resources.Member_Importing;

            // Phase 1: 检查数据范围，超出阈值则弹窗询问
            int scanRows, scanCols;
            try
            {
                var (totalRows, totalCols) = await _facade.GetDataSourceDimensionsAsync(FilePath, ct);
                if (totalRows > MaxAutoScanSize || totalCols > MaxAutoScanSize)
                {
                    var choice = await Dialog.ShowMultiOptionAsync(
                        Resources.Member_ImportRangeTooLarge,
                        string.Format(Resources.Member_ImportRangeTooLargeMsg, totalRows, totalCols),
                        Resources.Member_ImportFullScan,
                        Resources.Member_ImportLimitedScan);
                    if (choice == 0) // 完全扫描
                    {
                        scanRows = totalRows;
                        scanCols = totalCols;
                    }
                    else // 仅扫描前 70×70（或取消）
                    {
                        scanRows = Math.Min(totalRows, MaxAutoScanSize);
                        scanCols = Math.Min(totalCols, MaxAutoScanSize);
                    }
                }
                else
                {
                    scanRows = totalRows;
                    scanCols = totalCols;
                }
            }
            catch
            {
                // 维度读取失败时使用默认值，让 LoadStudentsAsync 自行处理
                scanRows = MaxAutoScanSize;
                scanCols = MaxAutoScanSize;
            }

            // Phase 2: 加载学生数据（使用 Phase 1 确定的范围）
            var students = await _facade.LoadStudentsAsync(FilePath, scanRows, scanCols, ct);

            ReplaceStudents(students);
            StatusMessage = IsEmpty ? Resources.Member_NoImport : $"已导入 {StudentCount} 名学生";

            // 自动保存到托管存储
            if (!IsEmpty)
            {
                var name = Path.GetFileNameWithoutExtension(FilePath);
                CurrentDatasetId = await _facade.SaveStudentDatasetAsync(name, students, Path.GetFileName(FilePath), ct);
                CurrentDatasetName = name;
                MarkClean();
                _ = RefreshDatasetsQuietAsync(ct);
            }

            if (IsEmpty)
            {
                errorTitle = Resources.Member_ImportResult;
                errorMsg = Resources.Member_NoNameField;
            }

            return !IsEmpty;
        }
        catch (Exception ex)
        {
            errorTitle = Resources.Member_ImportFailed;
            errorMsg = ex is FileNotFoundException
                ? string.Format(Resources.Member_FileNotFoundFmt, FilePath)
                : string.Format(Resources.Member_ImportErrorFmt, ex.Message);
            StatusMessage = Resources.Member_ImportFailed;
            return false;
        }
        finally
        {
            IsLoading = false;
            if (errorTitle != null)
            {
                // 导入失败：显示带帮助按钮的对话框
                var choice = await Dialog.ShowMultiOptionAsync(
                    errorTitle, errorMsg!,
                    Resources.Common_OK,
                    Resources.Member_ViewHelp);
                if (choice == 1) // 查看帮助
                    OpenHelpDocs();
            }
        }
    }

    // ═══════════════════════════════════════════════
    //  IFileDropHandler
    // ═══════════════════════════════════════════════

    IReadOnlyList<string> IFileDropHandler.AcceptedFileExtensions { get; } =
        [".csv", ".xlsx", ".json"];

    async Task<bool> IFileDropHandler.HandleFileDropAsync(IReadOnlyList<string> filePaths, CancellationToken ct)
    {
        var result = false;
        await _dialogGate.RunAsync(async () => { result = await ImportFromPathAsync(filePaths[0], ct); });
        return result;
    }

    /// <summary>从文件更新当前数据集，保持 CurrentDatasetId 不变。</summary>
    [RelayCommand]
    private async Task UpdateFromFileAsync(CancellationToken ct)
    {
        await _dialogGate.RunAsync(async () =>
        {
            string? errorTitle = null;
            string? errorMsg = null;

            try
            {
                string? importPath;
                try { importPath = await _fileService.OpenFilePathAsync(Resources.Member_UpdateFromFile, StudentFileTypes); }
                catch (Exception ex) { _logger.LogDebug(ex, "文件对话框取消或异常: 打开文件"); return; }
                if (importPath is null) return;

                FilePath = importPath;
                IsLoading = true;
                ErrorMessage = string.Empty;
                StatusMessage = "正在更新...";

                var students = await _facade.LoadStudentsAsync(FilePath, ct);

                ReplaceStudents(students);
                StatusMessage = IsEmpty ? "文件中无有效数据" : $"已从文件更新 {StudentCount} 名学生";

                // 保存到托管存储
                if (!IsEmpty)
                {
                    var name = CurrentDatasetName ?? Path.GetFileNameWithoutExtension(FilePath);
                    if (CurrentDatasetId is not null)
                    {
                        // 已有关联数据集 → 原地更新，保持 ID 不变
                        await _facade.UpdateStudentDatasetAsync(CurrentDatasetId, name, students,
                            Path.GetFileName(FilePath), ct);
                    }
                    else
                    {
                        // 无关联数据集（未保存的编辑区数据）→ 另存为新数据集
                        CurrentDatasetId = await _facade.SaveStudentDatasetAsync(name, students,
                            Path.GetFileName(FilePath), ct);
                        CurrentDatasetName = name;
                    }
                    MarkClean();
                    _ = RefreshDatasetsQuietAsync(ct);
                }

                if (IsEmpty)
                {
                    errorTitle = "更新结果";
                    errorMsg = "文件中未找到有效学生数据。";
                }
            }
            catch (Exception ex)
            {
                errorTitle = Resources.Member_ImportFailed;
                errorMsg = ex is FileNotFoundException
                    ? string.Format(Resources.Member_FileNotFoundFmt, FilePath)
                    : $"更新失败：{ex.Message}";
                StatusMessage = "更新失败";
            }
            finally
            {
                IsLoading = false;
                if (errorTitle != null)
                    await _dialog.ShowErrorAsync(errorTitle, errorMsg!);
            }
        });
    }

    [RelayCommand]
    private async Task ExportCsvAsync(CancellationToken ct)
    {
        await ExportAsync(ExportFormat.Csv, [new(Resources.Data_CSVFile) { Patterns = ["*.csv"] }], ct);
    }

    [RelayCommand]
    private async Task ExportExcelAsync(CancellationToken ct)
    {
        await ExportAsync(ExportFormat.Excel, [new(Resources.Data_ExcelFile) { Patterns = ["*.xlsx"] }], ct);
    }

    [RelayCommand]
    private async Task ExportJsonAsync(CancellationToken ct)
    {
        await ExportAsync(ExportFormat.Json, [new(Resources.Data_JSONFile) { Patterns = ["*.json"] }], ct);
    }

    private async Task ExportAsync(ExportFormat format, FilePickerFileType[] types, CancellationToken ct)
    {
        await _dialogGate.RunAsync(async () =>
        {
            string? errorTitle = null;
            string? errorMsg = null;

            if (Students.Count == 0)
            {
                await _dialog.ShowWarningAsync("无数据", Resources.Member_NoDataToExport);
                return;
            }

            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;
                StatusMessage = Resources.Member_Exporting;

                if (OperatingSystem.IsBrowser())
                {
                    // WASM：导出到内存文件系统临时文件 → 读取字节 → 浏览器下载
                    var ext = format switch
                    {
                        ExportFormat.Excel => ".xlsx",
                        ExportFormat.Json => ".json",
                        _ => ".csv"
                    };
                    var tempPath = Path.Combine(Path.GetTempPath(), $"seatflow-members-{Guid.NewGuid():N}{ext}");
                    try
                    {
                        await _facade.ExportStudentsAsync(tempPath, GetStudents(), format, ct);
                        var bytes = await File.ReadAllBytesAsync(tempPath, ct);
                        var downloadName = $"seatflow_members_{DateTime.Now:yyyyMMdd_HHmm}{ext}";
                        await _fileService.SaveFileBytesAsync(downloadName, bytes, types);
                        StatusMessage = Resources.Member_ExportDone;
                    }
                    finally
                    {
                        try { File.Delete(tempPath); } catch { /* 临时文件清理失败可忽略 */ }
                    }
                    return;
                }

                IStorageFile? exportFile;
                try { exportFile = await _fileService.SaveFileAsync(Resources.Data_Export, types); }
                catch (Exception ex) { _logger.LogDebug(ex, "文件对话框取消或异常: 导出CSV"); return; }
                if (exportFile is null) return;
                var file = exportFile;

                await _facade.ExportStudentsAsync(file.Path.LocalPath, GetStudents(), format, ct);

                StatusMessage = Resources.Member_ExportDone;
            }
            catch (Exception ex)
            {
                errorTitle = Resources.Member_ExportFailed;
                errorMsg = string.Format(Resources.Member_ExportErrorFmt, ex.Message);
                StatusMessage = Resources.Member_ExportFailed;
            }
            finally
            {
                IsLoading = false;
                if (errorTitle != null)
                    await _dialog.ShowErrorAsync(errorTitle, errorMsg!);
            }
        });
    }

    [RelayCommand]
    private async Task ClearDataAsync()
    {
        await ClearDataInternalAsync();
    }

    /// <summary>卸载数据的核心逻辑（可由取消选中或按钮触发）。仅 IsDirty 时弹确认窗。</summary>
    private async Task ClearDataInternalAsync()
    {
        if (IsDirty)
        {
            var confirmed = await _dialog.ShowConfirmAsync(Resources.Member_ClearConfirm,
                string.Format(Resources.Member_ClearConfirmMsg, StudentCount));
            if (!confirmed) return;
        }

        ReplaceStudents([]);
        CurrentDatasetId = null;
        CurrentDatasetName = null;
        FilePath = string.Empty;
        ErrorMessage = string.Empty;
        _dirty.Reset();
        NewStudent = new Student();
        StatusMessage = Resources.Member_Ready;
    }

    public override async Task<bool> CanLeaveAsync()
    {
        if (!IsDirty)
        {
            await ClearDataInternalAsync();
            return true;
        }

        var choice = await Dialog.ShowMultiOptionAsync(
            Resources.Member_UnsavedChanges,
            Resources.Member_UnsavedChangesMsg,
            Resources.Common_Save,
            Resources.Common_Discard,
            Resources.Common_Cancel);

        switch (choice)
        {
            case 0: // 保存
                await SaveInternalAsync(CancellationToken.None);
                break;
            case 1: // 放弃
                break;
            default: // 取消
                return false;
        }

        await ClearDataInternalAsync();
        return true;
    }

    [RelayCommand]
    private void BeginEdit(StudentRowViewModel? row)
    {
        if (row is null) return;
        foreach (var item in Students)
        {
            if (ReferenceEquals(item, row))
            {
                // 记录进入编辑态前的值，供 Esc 取消回滚
                row.SnapshotForEdit();
                item.IsEditing = true;
            }
            else
            {
                item.IsEditing = false;
            }
        }
    }

    [RelayCommand]
    private void EndEdit(StudentRowViewModel? row)
    {
        if (row is null) return;
        row.ClearEditSnapshot();
        row.IsEditing = false;
        MoveToSortedPosition(row);
        RefreshDirty();
    }

    /// <summary>取消行内编辑：回滚到进入编辑态前的值并退出编辑态（Esc）。</summary>
    [RelayCommand]
    private void CancelEdit(StudentRowViewModel? row)
    {
        if (row is null) return;
        row.CancelEdit();
        RefreshDirty();
    }

    [RelayCommand]
    private void DeleteStudent(StudentRowViewModel row)
    {
        row.PropertyChanged -= OnRowPropertyChanged;
        if (Students.Remove(row))
        {
            _dirty.MarkDirty();
            RefreshDirty();
            StudentCount = Students.Count;
            IsEmpty = StudentCount == 0;
            UseVirtualization = StudentCount > 300;
            StatusMessage = string.Format(Resources.Member_DeletedRowFmt, row.Student.Name, StudentCount);
        }
    }

    [RelayCommand]
    private void AddNewStudent()
    {
        if (string.IsNullOrWhiteSpace(NewStudent.Name))
            return;

        var row = new StudentRowViewModel(new Student
        {
            Name = NewStudent.Name.Trim(),
            Height = NewStudent.Height,
            Gender = NewStudent.Gender,
            NeedsFrontRow = NewStudent.NeedsFrontRow
        });
        row.PropertyChanged += OnRowPropertyChanged;

        // 按显示排序插入，保持列表始终有序（与加载时一致）
        int insertIndex = 0;
        while (insertIndex < Students.Count &&
               CompareStudents(Students[insertIndex].Student, row.Student) <= 0)
            insertIndex++;
        Students.Insert(insertIndex, row);

        _dirty.MarkDirty();
        RefreshDirty();

        NewStudent = new Student();
        StudentCount = Students.Count;
        IsEmpty = false;
        UseVirtualization = Students.Count > 300;
        StatusMessage = string.Format(Resources.Member_AddedRowFmt, StudentCount);
    }

    private async Task LoadDatasetAsync(StudentDatasetInfo dataset, CancellationToken ct)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = Resources.Member_Loading;

        try
        {
            var students = await _facade.LoadStudentDatasetAsync(dataset.Id, ct);
            if (students is not null)
            {
                CurrentDatasetId = dataset.Id;
                CurrentDatasetName = dataset.Name;
                ReplaceStudents(students);
                FilePath = dataset.OriginalFileName ?? dataset.Name;
                MarkClean();
                NewStudent = new Student();
                StatusMessage = StudentCount > 0
                    ? string.Format(Resources.Member_LoadedFmt, StudentCount)
                    : Resources.Member_EmptyDataset;
            }
            else
            {
                StatusMessage = Resources.Member_DatasetNotFound;
                await _dialog.ShowErrorAsync(Resources.Data_LoadFailed, $"找不到数据集「{dataset.Name}」的文件。");
                await RefreshDatasetsAsync(ct);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.Data_LoadFailed;
            await _dialog.ShowErrorAsync(Resources.Data_LoadFailed, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedDatasetAsync(CancellationToken ct)
    {
        if (SelectedDataset is null) return;

        var confirmed = await _dialog.ShowConfirmAsync(Resources.Data_DeleteConfirm,
            string.Format(Resources.Member_DeleteConfirmMsg, SelectedDataset.Name));
        if (!confirmed) return;

        try
        {
            await _facade.DeleteStudentDatasetAsync(SelectedDataset.Id, ct);
            SelectedDataset = null;
            await RefreshDatasetsAsync(ct);
            StatusMessage = Resources.Member_Deleted;
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(Resources.Member_DeleteFailed, ex.Message);
        }
    }

    [RelayCommand]
    private async Task RenameSelectedDatasetAsync(CancellationToken ct)
    {
        if (SelectedDataset is null) return;

        var (confirmed, newName) = await _dialog.ShowInputAsync(Resources.Member_RenameTitle,
            string.Format(Resources.Member_RenamePrompt, SelectedDataset.Name), SelectedDataset.Name);
        if (!confirmed || string.IsNullOrWhiteSpace(newName)) return;

        try
        {
            await _facade.RenameStudentDatasetAsync(SelectedDataset.Id, newName.Trim(), ct);

            if (CurrentDatasetId == SelectedDataset.Id)
                CurrentDatasetName = newName.Trim();

            SelectedDataset = null;
            await RefreshDatasetsAsync(ct);
            StatusMessage = Resources.Member_Renamed;
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(Resources.Member_RenameFailed, ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct)
    {
        if (Students.Count == 0) return;

        var errors = ValidateStudents();
        if (errors.Count > 0)
        {
            await _dialog.ShowErrorAsync(Resources.Data_ValidationFailed,
                string.Join('\n', errors.Take(10)));
            return;
        }

        // 检查空行是否有未完成的数据
        if (IsNewStudentDirty)
        {
            var choice = await Dialog.ShowMultiOptionAsync(
                Resources.Member_NewRowPendingTitle,
                Resources.Member_NewRowPendingMsg,
                Resources.Member_DiscardAndSave,
                Resources.Common_Cancel);
            if (choice != 0) return;
            NewStudent = new Student();
        }

        // 无关联数据集 → 另存为
        if (CurrentDatasetId is null)
        {
            await RenameSaveAsync(ct);
            return;
        }

        await SaveInternalAsync(ct);
    }

    /// <summary>直接保存到 CurrentDatasetId，无确认弹窗，不刷新侧栏选中状态。</summary>
    private async Task SaveInternalAsync(CancellationToken ct)
    {
        var datasetName = CurrentDatasetName ?? Resources.Member_Unnamed;

        try
        {
            if (CurrentDatasetId is not null)
                await _facade.DeleteStudentDatasetAsync(CurrentDatasetId, ct);

            CurrentDatasetId = await _facade.SaveStudentDatasetAsync(datasetName, GetStudents(), null, ct);
            CurrentDatasetName = datasetName;
            MarkClean();
            await RefreshDatasetsAsync(ct);
            StatusMessage = string.Format(Resources.Member_SavedFmt, datasetName);
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(Resources.Data_SaveFailed, ex.Message);
        }
    }

    private List<string> ValidateStudents()
    {
        var errors = new List<string>();

        for (int i = 0; i < Students.Count; i++)
        {
            var s = Students[i].Student;
            var row = i + 1;

            // 跳过完全空行（所有字段均未填写）
            if (string.IsNullOrWhiteSpace(s.Name) && s.Height == null && s.Gender == null && !s.NeedsFrontRow)
                continue;

            if (string.IsNullOrWhiteSpace(s.Name))
                errors.Add(string.Format(Resources.Member_NameEmptyFmt, row));

            if (s.Height.HasValue && s.Height.Value <= 0)
                errors.Add(string.Format(Resources.Member_HeightInvalidFmt, row, s.Name));

            if (s.Gender.HasValue && !Enum.IsDefined(s.Gender.Value))
                errors.Add(string.Format(Resources.Member_GenderInvalidFmt, row, s.Name));
        }

        return errors;
    }

    [RelayCommand]
    private async Task RenameSaveAsync(CancellationToken ct)
    {
        var (confirmed, newName) = await _dialog.ShowInputAsync(Resources.Member_SaveAsTitle,
            Resources.Member_SaveAsPrompt, "");
        if (!confirmed || string.IsNullOrWhiteSpace(newName)) return;

        try
        {
            var newId = await _facade.SaveStudentDatasetAsync(newName.Trim(), GetStudents(), null, ct);
            CurrentDatasetId = newId;
            CurrentDatasetName = newName.Trim();
            MarkClean();
            await RefreshDatasetsAsync(ct);
            StatusMessage = string.Format(Resources.Member_SavedAsFmt, newName.Trim());
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(Resources.Data_SaveFailed, ex.Message);
        }
    }
}
