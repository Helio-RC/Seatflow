using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using ReactiveUI.Primitives;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Core.Telemetry;
using SeatFlow.Infrastructure.Layouts;
using SeatFlow.Presentation.Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>OOBE 步骤（交接阶段不属于全屏步骤）。</summary>
public enum OobeStep
{
    Welcome,
    Preferences,
    Roster,
    Venue,
}

/// <summary>欢迎屏步骤卡：编号、标题、说明与当前步高亮。</summary>
public sealed record WelcomeStepItem(string Number, string Title, string Description, bool IsActive);

/// <summary>交接卡状态。</summary>
public enum OobeHandoffState
{
    None,
    Prompt,
    Success,
    Failure,
    Incomplete,
}

/// <summary>
/// 首次设置状态机：欢迎/遥测 → 启动偏好（桌面）→ 名单 → 会场 → 交接。
/// 覆盖在 MainView 上，不新增 PageKey；真实写盘数据集/会场。
/// </summary>
public partial class OobeViewModel : ViewModelBase, IOobeService, IStudentRosterEditor
{
    private const int DefaultRows = VenueLayoutDefaults.GridRows;
    private const int DefaultColumns = VenueLayoutDefaults.GridColumns;
    private const int DefaultAisleWidth = (int)VenueLayoutDefaults.GridAisleWidth;
    private const int MaxGridSize = 100;

    private readonly IApplicationFacade _facade;
    private readonly IFileService _fileService;
    private readonly ITelemetryService _telemetry;
    private readonly INavigationService _navigation;
    private readonly IShellLayoutService _layout;
    private readonly StartupRestoreState _restoreState;
    private readonly ILogger<OobeViewModel> _logger;
    private readonly List<IDisposable> _subscriptions = [];

    private string? _createdDatasetId;
    private string? _loadedExistingDatasetId;
    private string? _loadedDatasetName;
    private bool _rosterDirty;
    private bool _suppressRosterTracking;
    private bool _rosterCollectionSubscribed;
    private string? _createdVenueId;
    private string? _editingVenueId;
    private string? _loadedVenueId;
    private VenueSummary? _loadedVenueSummary;
    private string? _importedFileName;
    private GridLayoutMetadata? _existingVenueBaseMetadata;
    private Dictionary<(int Row, int Column), string>? _existingGridSeatMap;
    private Dictionary<(int Row, int Column), string>? _createdGridSeatMap;
    private List<Obstacle>? _existingVenueObstacles;
    private bool _suppressVenueTracking;
    private bool _venueDirty;
    private int _venueEditRevision;
    private int _seatsPerDesk = VenueLayoutDefaults.GridSeatsPerDesk;
    private bool _completed;
    private bool _firstSuccessConsumed;
    private bool _missingDataset;
    private bool _missingVenue;
    private bool _restoreHintVisible;
    private SeatingArrangementViewModel? _handoffSeating;

    private IObservable<bool> NotBusy => this.WhenAnyValue(x => x.IsBusy, busy => !busy);

    private IObservable<bool> CanAdvanceAndNotBusy =>
        this.WhenAnyValue(x => x.IsBusy, x => x.CanProceed, (busy, can) => !busy && can);

    public OobeViewModel(
        IApplicationFacade facade,
        IFileService fileService,
        ITelemetryService telemetry,
        INavigationService navigation,
        IShellLayoutService layout,
        StartupRestoreState restoreState,
        IDialogService dialog,
        ILogger<OobeViewModel>? logger = null) : base(dialog, logger)
    {
        _facade = facade;
        _fileService = fileService;
        _telemetry = telemetry;
        _navigation = navigation;
        _layout = layout;
        _restoreState = restoreState;
        _logger = logger ?? NullLogger<OobeViewModel>.Instance;

        NextCommand = ReactiveCommand.CreateFromTask(AdvanceAsync, CanAdvanceAndNotBusy);
        BackCommand = ReactiveCommand.CreateFromTask(
            GoBackAsync,
            this.WhenAnyValue(x => x.IsBusy, x => x.CanGoBack, (busy, can) => !busy && can));
        SkipAllCommand = ReactiveCommand.CreateFromTask(SkipAllAsync, NotBusy);
        SkipStepCommand = ReactiveCommand.CreateFromTask(SkipStepAsync, NotBusy);
        ImportRosterCommand = ReactiveCommand.CreateFromTask(
            ImportRosterAsync,
            this.WhenAnyValue(x => x.IsBusy, x => x.IsRosterLoading, (busy, loading) => !busy && !loading));
        StartBlankRosterCommand = ReactiveCommand.Create(StartBlankRoster, NotBusy);
        BeginEditCommand = ReactiveCommand.Create<StudentRowViewModel?>(BeginEdit, NotBusy);
        EndEditCommand = ReactiveCommand.Create<StudentRowViewModel?>(EndEdit, NotBusy);
        CancelEditCommand = ReactiveCommand.Create<StudentRowViewModel?>(CancelEdit, NotBusy);
        DeleteStudentCommand = ReactiveCommand.Create<StudentRowViewModel?>(DeleteStudent, NotBusy);
        AddNewStudentCommand = ReactiveCommand.Create(AddNewStudent, NotBusy);
        UseExistingRosterCommand = ReactiveCommand.CreateFromTask<StudentDatasetInfo?>(
            UseExistingRosterAsync,
            this.WhenAnyValue(x => x.IsBusy, (busy) => !busy));
        SelectExistingVenueCommand = ReactiveCommand.CreateFromTask<VenueSummary?>(
            summary => RunBusyAsync(() => SelectExistingVenueAsync(summary)),
            this.WhenAnyValue(x => x.IsBusy, (busy) => !busy));
        NewVenueCommand = ReactiveCommand.Create(StartNewVenue, NotBusy);
        HandoffPrimaryCommand = ReactiveCommand.CreateFromTask(HandoffPrimaryAsync, NotBusy);
        HandoffSecondaryCommand = ReactiveCommand.CreateFromTask(HandoffSecondaryAsync, NotBusy);

        _navigation.CurrentViewModelChanged += OnNavigationChanged;

        WelcomeStep1 = new("1", Resources.Oobe_WelcomePoint1Title, Resources.Oobe_WelcomePoint1Desc, true);
        WelcomeStep2 = new("2", Resources.Oobe_WelcomePoint2Title, Resources.Oobe_WelcomePoint2Desc, false);
        WelcomeStep3 = new("3", Resources.Oobe_WelcomePoint3Title, Resources.Oobe_WelcomePoint3Desc, false);

        SetRosterRows([]);
        RebuildAisleOptions([], []);
        RebuildColumnRowCountOptions([]);
        RefreshState();

        // 预览去抖：与会场页一致（120ms，UI 线程）。
        _subscriptions.Add(
            this.WhenAnyValue(x => x.PreviewRevision)
                .Throttle(TimeSpan.FromMilliseconds(120), RxSchedulers.MainThreadScheduler)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .SubscribeSafe(
                    _ => RecomputePreviewNow(),
                    ex => _logger.LogWarning(ex, "OOBE 会场预览调度失败")));
    }

    // ── 状态 ──

    [ObservableProperty]
    public partial OobeStep Step { get; set; } = OobeStep.Welcome;

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial bool IsHandoffVisible { get; set; }

    [ObservableProperty]
    public partial bool IsRestart { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool TelemetryEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowTelemetryQuestion { get; set; } = true;

    [ObservableProperty]
    public partial int AutoUpdateIndex { get; set; } = (int)AutoUpdateMode.AutoUpdate;

    [ObservableProperty]
    public partial bool IsAutoUpdateVisible { get; set; } = !OperatingSystem.IsBrowser();

    public ObservableCollection<StudentRowViewModel> RosterRows { get; } = [];

    /// <summary>共享名单表格契约：OOBE 直接使用导入顺序，不按姓名重排。</summary>
    public ObservableCollection<StudentRowViewModel> Students => RosterRows;

    /// <summary>底部新增行的绑定源。</summary>
    [ObservableProperty]
    public partial Student NewStudent { get; set; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseStaticRoster))]
    public partial bool UseVirtualization { get; set; }

    public bool UseStaticRoster => !UseVirtualization;

    [ObservableProperty]
    public partial ObservableCollection<StudentDatasetInfo> ExistingDatasets { get; set; } = [];

    [ObservableProperty]
    public partial StudentDatasetInfo? SelectedExistingDataset { get; set; }

    [ObservableProperty]
    public partial string DatasetName { get; set; } = Resources.Oobe_RosterDefaultName;

    [ObservableProperty]
    public partial string? SelectedDatasetId { get; set; }

    [ObservableProperty]
    public partial bool IsRosterLoading { get; set; }

    [ObservableProperty]
    public partial string? RosterError { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<VenueSummary> ExistingVenues { get; set; } = [];

    [ObservableProperty]
    public partial VenueSummary? SelectedExistingVenue { get; set; }

    [ObservableProperty]
    public partial bool IsExistingVenueReadOnly { get; set; }

    [ObservableProperty]
    public partial bool IsEditingExistingVenue { get; set; }

    [ObservableProperty]
    public partial bool IsVenueLoading { get; set; }

    [ObservableProperty]
    public partial string? VenueError { get; set; }

    [ObservableProperty]
    public partial string VenueName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int Rows { get; set; } = DefaultRows;

    [ObservableProperty]
    public partial int Columns { get; set; } = DefaultColumns;

    [ObservableProperty]
    public partial int AisleWidth { get; set; } = DefaultAisleWidth;

    [ObservableProperty]
    public partial ObservableCollection<AisleOption> AisleColumnOptions { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<AisleOption> AisleRowOptions { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<ColumnRowCountOption> ColumnRowCountOptions { get; set; } = [];

    [ObservableProperty]
    public partial SeatLayoutSnapshot? PreviewSnapshot { get; set; }

    [ObservableProperty]
    public partial int PreviewSeatCount { get; set; }

    [ObservableProperty]
    public partial long PreviewRevision { get; set; }

    [ObservableProperty]
    public partial OobeHandoffState HandoffState { get; set; }

    [ObservableProperty]
    public partial string HandoffTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HandoffMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HandoffPrimaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HandoffSecondaryText { get; set; } = string.Empty;

    // ── 命令 ──

    public ReactiveCommand<RxVoid, RxVoid> NextCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> BackCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SkipAllCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SkipStepCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ImportRosterCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> StartBlankRosterCommand { get; }
    public ReactiveCommand<StudentRowViewModel?, RxVoid> BeginEditCommand { get; }
    public ReactiveCommand<StudentRowViewModel?, RxVoid> EndEditCommand { get; }
    public ReactiveCommand<StudentRowViewModel?, RxVoid> CancelEditCommand { get; }
    public ReactiveCommand<StudentRowViewModel?, RxVoid> DeleteStudentCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddNewStudentCommand { get; }
    public ReactiveCommand<StudentDatasetInfo?, RxVoid> UseExistingRosterCommand { get; }
    public ReactiveCommand<VenueSummary?, RxVoid> SelectExistingVenueCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewVenueCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> HandoffPrimaryCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> HandoffSecondaryCommand { get; }

    // ── 计算属性 ──

    public bool IsActive => IsVisible || IsHandoffVisible;

    public bool IsFullScreenVisible => IsVisible;

    public IShellLayoutService Layout => _layout;

    public IReadOnlyList<string> AutoUpdateOptions { get; } =
    [
        Resources.Settings_AutoUpdate_Off,
        Resources.Settings_AutoUpdate_CheckOnly,
        Resources.Settings_AutoUpdate_Auto,
    ];

    public WelcomeStepItem WelcomeStep1 { get; }

    public WelcomeStepItem WelcomeStep2 { get; }

    public WelcomeStepItem WelcomeStep3 { get; }

    public bool IsWelcomeStep => Step == OobeStep.Welcome;

    public bool IsPreferencesStep => Step == OobeStep.Preferences;

    public bool IsRosterStep => Step == OobeStep.Roster;

    public bool IsVenueStep => Step == OobeStep.Venue;

    public OobeStep? PreviousStep => Step switch
    {
        OobeStep.Welcome => null,
        OobeStep.Preferences => OobeStep.Welcome,
        OobeStep.Roster => IsRestart
            ? null
            : (IsAutoUpdateVisible ? OobeStep.Preferences : OobeStep.Welcome),
        OobeStep.Venue => OobeStep.Roster,
        _ => null,
    };

    public bool CanGoBack => PreviousStep is not null;

    public bool HasRoster => RosterRows.Any(r => !string.IsNullOrWhiteSpace(r.Name));

    /// <summary>列表中选择了尚未载入的名单（选择 ≠ 已加载）。</summary>
    public bool IsRosterSelectionPending => SelectedExistingDataset is not null
        && !string.Equals(SelectedExistingDataset.Id, _loadedExistingDatasetId, StringComparison.Ordinal);

    /// <summary>列表中选择了尚未载入的会场（选择 ≠ 已加载）。</summary>
    public bool IsVenueSelectionPending => SelectedExistingVenue is not null
        && !string.Equals(SelectedExistingVenue.Id, _loadedVenueId, StringComparison.Ordinal);

    public bool CanSaveVenue
    {
        get
        {
            if (IsVenueLoading || IsVenueSelectionPending) return false;
            if (IsExistingVenueReadOnly) return _loadedVenueId is not null;
            return !string.IsNullOrWhiteSpace(VenueName)
                && Rows is >= 1 and <= MaxGridSize
                && Columns is >= 1 and <= MaxGridSize;
        }
    }

    public bool CanProceed => !IsBusy && Step switch
    {
        OobeStep.Welcome => true,
        OobeStep.Preferences => true,
        OobeStep.Roster => !IsRosterLoading && !IsRosterSelectionPending && HasRoster,
        OobeStep.Venue => !IsVenueLoading && !IsVenueSelectionPending && CanSaveVenue,
        _ => false,
    };

    public bool IsVenuePreviewVisible => !IsExistingVenueReadOnly;

    public string RosterPrimaryText => _loadedExistingDatasetId is not null
        ? Resources.Oobe_UseRoster
        : Resources.Oobe_SaveAndContinue;

    public string VenuePrimaryText => IsExistingVenueReadOnly
        || (_loadedVenueId is not null && !_venueDirty)
        ? Resources.Oobe_UseVenue
        : Resources.Oobe_SaveAndContinue;

    public string PreviewSeatCountText => string.Format(Resources.Oobe_VenueSeatCount, PreviewSeatCount);

    public bool IsHandoffPrompt => HandoffState == OobeHandoffState.Prompt;
    public bool IsHandoffSuccess => HandoffState == OobeHandoffState.Success;
    public bool IsHandoffFailure => HandoffState == OobeHandoffState.Failure;
    public bool IsHandoffIncomplete => HandoffState == OobeHandoffState.Incomplete;

    public string RestoredHint => _restoreHintVisible
        ? string.Format(Resources.Oobe_RosterRestoredHint, _restoreState.RestoredFiles)
        : string.Empty;

    // ── 启动 / 完成 ──

    public async Task StartAsync(bool isRestart = false)
    {
        _completed = false;
        _firstSuccessConsumed = false;
        _missingDataset = false;
        _missingVenue = false;
        _createdDatasetId = null;
        _loadedExistingDatasetId = null;
        _createdVenueId = null;
        _editingVenueId = null;
        _loadedVenueId = null;
        _loadedVenueSummary = null;
        _importedFileName = null;
        _existingVenueBaseMetadata = null;
        _existingGridSeatMap = null;
        _createdGridSeatMap = null;
        _existingVenueObstacles = null;
        _venueDirty = false;
        _venueEditRevision = 0;
        _restoreHintVisible = !isRestart && _restoreState.SeatSetsImported;
        HandoffState = OobeHandoffState.None;
        ErrorMessage = null;
        RosterError = null;
        VenueError = null;
        SelectedExistingDataset = null;
        SelectedDatasetId = null;
        SelectedExistingVenue = null;
        _loadedDatasetName = null;
        _rosterDirty = false;
        IsExistingVenueReadOnly = false;
        IsEditingExistingVenue = false;
        IsRosterLoading = false;
        IsVenueLoading = false;
        IsHandoffVisible = false;
        DetachHandoff();

        var settings = await _facade.LoadAppSettingsAsync();

        IsRestart = isRestart;
        ShowTelemetryQuestion = !settings.Telemetry.ConsentShown && !isRestart;
        TelemetryEnabled = ShowTelemetryQuestion ? true : settings.Telemetry.Enabled;
        AutoUpdateIndex = settings.AutoUpdate switch
        {
            AutoUpdateMode.Off => 0,
            AutoUpdateMode.CheckOnly => 1,
            _ => 2,
        };
        IsAutoUpdateVisible = !OperatingSystem.IsBrowser();

        DatasetName = Resources.Oobe_RosterDefaultName;
        SetRosterRows([]);
        ResetVenueDefaults();

        Step = isRestart ? OobeStep.Roster : OobeStep.Welcome;
        IsVisible = true;
        RefreshState();
        IsBusy = true;
        try
        {
            await EnterStepAsync(Step);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CompleteAsync(bool skipped)
    {
        if (_completed) return;

        if (!await TryMarkFirstLaunchCompletedAsync())
        {
            ErrorMessage = Resources.Oobe_ErrorComplete;
            if (IsHandoffVisible)
                HandoffMessage = ErrorMessage;
            return;
        }

        _completed = true;
        _logger.LogInformation("[OOBE] 完成（skipped={Skipped}）", skipped);
        IsVisible = false;
        IsHandoffVisible = false;
        DetachHandoff();
        RefreshState();
        Completed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Completed;

    // ── 步骤推进 ──

    private Task AdvanceAsync() => RunBusyAsync(async () =>
    {
        ErrorMessage = null;
        var step = Step;
        switch (step)
        {
            case OobeStep.Welcome:
                if (!await SaveTelemetryAsync(TelemetryEnabled)) return;
                if (Step != step) return;
                await GoToStepAsync(IsAutoUpdateVisible ? OobeStep.Preferences : OobeStep.Roster);
                break;

            case OobeStep.Preferences:
                if (!await SaveAutoUpdateAsync()) return;
                if (Step != step) return;
                await GoToStepAsync(OobeStep.Roster);
                break;

            case OobeStep.Roster:
                if (!await SaveRosterAsync()) return;
                if (Step != step) return;
                await GoToStepAsync(OobeStep.Venue);
                break;

            case OobeStep.Venue:
                if (!await SaveVenueAsync()) return;
                if (Step != step) return;
                await StartHandoffAsync();
                break;
        }
    });

    private Task GoBackAsync() => RunBusyAsync(async () =>
    {
        var step = Step;
        if (PreviousStep is not { } previous) return;
        ErrorMessage = null;
        if (Step != step) return;
        await GoToStepAsync(previous);
    });

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await action();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task GoToStepAsync(OobeStep step)
    {
        Step = step;
        RefreshState();
        await EnterStepAsync(step);
    }

    private async Task EnterStepAsync(OobeStep step)
    {
        switch (step)
        {
            case OobeStep.Roster:
                await LoadDatasetsAsync();
                break;
            case OobeStep.Venue:
                await LoadVenuesAsync();
                break;
        }
    }

    private Task SkipAllAsync() => RunBusyAsync(async () =>
    {
        var step = Step;
        // “跳过设置”不能被默认勾选静默视为同意遥测：按关闭处理，避免未经选择即启用上报。
        if (ShowTelemetryQuestion && !await SaveTelemetryAsync(false)) return;
        if (Step != step) return;
        await CompleteAsync(skipped: true);
    });

    private Task SkipStepAsync() => RunBusyAsync(async () =>
    {
        ErrorMessage = null;
        var step = Step;
        switch (step)
        {
            case OobeStep.Roster:
                await GoToStepAsync(OobeStep.Venue);
                break;
            case OobeStep.Venue:
                await StartHandoffAsync();
                break;
            default:
                if (ShowTelemetryQuestion && !await SaveTelemetryAsync(false)) return;
                if (Step != step) return;
                await CompleteAsync(skipped: true);
                break;
        }
    });

    // ── 欢迎 / 偏好 ──

    private async Task<bool> SaveTelemetryAsync(bool enabled)
    {
        if (!ShowTelemetryQuestion) return true;

        var ok = await SaveSettingsAsync(settings =>
        {
            settings.Telemetry.ConsentShown = true;
            settings.Telemetry.Enabled = enabled;
        }, Resources.Oobe_ErrorSettingsSave);

        if (ok)
        {
            ShowTelemetryQuestion = false;
            TelemetryEnabled = enabled;
            _telemetry.ApplyEnabled(enabled);
        }

        return ok;
    }

    private async Task<bool> SaveAutoUpdateAsync()
    {
        if (!IsAutoUpdateVisible) return true;

        var mode = AutoUpdateIndex switch
        {
            0 => AutoUpdateMode.Off,
            1 => AutoUpdateMode.CheckOnly,
            _ => AutoUpdateMode.AutoUpdate,
        };

        return await SaveSettingsAsync(s => s.AutoUpdate = mode, Resources.Oobe_ErrorSettingsSave);
    }

    // ── 名单 ──

    private Task ImportRosterAsync() => RunBusyAsync(async () =>
    {
        if (IsRosterLoading) return;
        ErrorMessage = null;
        RosterError = null;
        string? path;
        try
        {
            path = await _fileService.OpenFilePathAsync(
                Resources.Oobe_ImportRosterTitle,
                [new FilePickerFileType("Excel") { Patterns = ["*.xlsx"] }]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OOBE 打开名单文件失败");
            RosterError = ex.Message;
            return;
        }

        if (string.IsNullOrEmpty(path)) return;

        IsRosterLoading = true;
        RefreshState();
        try
        {
            var students = await _facade.LoadStudentsAsync(path);
            _importedFileName = Path.GetFileName(path);
            DatasetName = Path.GetFileNameWithoutExtension(path);
            SelectedExistingDataset = null;
            SelectedDatasetId = null;
            _createdDatasetId = null;
            _loadedExistingDatasetId = null;
            _loadedDatasetName = null;
            SetRosterRows(students);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OOBE 导入名单失败");
            RosterError = ex.Message;
        }
        finally
        {
            IsRosterLoading = false;
            RefreshState();
        }
    });

    private void StartBlankRoster()
    {
        ErrorMessage = null;
        RosterError = null;
        _importedFileName = null;
        DatasetName = Resources.Oobe_RosterDefaultName;
        SelectedExistingDataset = null;
        SelectedDatasetId = null;
        _createdDatasetId = null;
        _loadedExistingDatasetId = null;
        _loadedDatasetName = null;
        SetRosterRows([]);
        RefreshState();
    }

    private void BeginEdit(StudentRowViewModel? row)
    {
        if (row is null) return;

        foreach (var item in RosterRows)
        {
            if (ReferenceEquals(item, row))
            {
                row.SnapshotForEdit();
                item.IsEditing = true;
            }
            else
            {
                item.IsEditing = false;
            }
        }
    }

    private void EndEdit(StudentRowViewModel? row)
    {
        if (row is null) return;

        row.ClearEditSnapshot();
        row.IsEditing = false;
        RefreshState();
    }

    private void CancelEdit(StudentRowViewModel? row)
    {
        if (row is null) return;

        row.CancelEdit();
        RefreshState();
    }

    private void DeleteStudent(StudentRowViewModel? row)
    {
        if (row is null) return;

        RosterRows.Remove(row);
        RefreshState();
    }

    private void AddNewStudent()
    {
        if (string.IsNullOrWhiteSpace(NewStudent.Name)) return;

        RosterRows.Add(new StudentRowViewModel(new Student
        {
            Name = NewStudent.Name.Trim(),
            Height = NewStudent.Height,
            Gender = NewStudent.Gender,
            NeedsFrontRow = NewStudent.NeedsFrontRow,
        }));
        NewStudent = new Student();
        RefreshState();
    }

    private Task UseExistingRosterAsync(StudentDatasetInfo? dataset) => RunBusyAsync(async () =>
    {
        if (dataset is null || IsRosterLoading) return;

        ErrorMessage = null;
        RosterError = null;
        IsRosterLoading = true;
        RefreshState();
        try
        {
            var students = await _facade.LoadStudentDatasetAsync(dataset.Id);
            if (students is null)
            {
                RosterError = Resources.Oobe_ErrorRosterLoad;
                RevertRosterSelection();
                return;
            }

            SelectedExistingDataset = dataset;
            SelectedDatasetId = dataset.Id;
            _createdDatasetId = null;
            _loadedExistingDatasetId = dataset.Id;
            _loadedDatasetName = dataset.Name;
            _importedFileName = dataset.OriginalFileName;
            DatasetName = dataset.Name;
            SetRosterRows(students);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OOBE 加载已有名单失败");
            RosterError = ex.Message;
            RevertRosterSelection();
        }
        finally
        {
            IsRosterLoading = false;
            RefreshState();
        }
    });

    private void RevertRosterSelection()
    {
        SelectedExistingDataset = _loadedExistingDatasetId is null
            ? null
            : ExistingDatasets.FirstOrDefault(d => d.Id == _loadedExistingDatasetId);
    }

    private async Task<bool> SaveRosterAsync()
    {
        RosterError = null;

        if (IsRosterSelectionPending)
        {
            RosterError = Resources.Oobe_ErrorRosterLoad;
            return false;
        }

        var students = RosterRows
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => r.Student)
            .ToList();

        if (students.Count == 0)
        {
            RosterError = Resources.Oobe_ErrorRosterEmpty;
            return false;
        }

        var name = string.IsNullOrWhiteSpace(DatasetName)
            ? Resources.Oobe_RosterDefaultName
            : DatasetName.Trim();

        try
        {
            if (_loadedExistingDatasetId is not null)
            {
                // 已有名单：仅在有编辑时原地更新，保留 ID 与原始文件名。
                if (_rosterDirty)
                {
                    await _facade.UpdateStudentDatasetAsync(
                        _loadedExistingDatasetId, name, students, _importedFileName);
                    _loadedDatasetName = name;
                    DatasetName = name;
                    _rosterDirty = false;
                }

                SelectedDatasetId = _loadedExistingDatasetId;
                RefreshState();
                return true;
            }

            if (_createdDatasetId is null)
            {
                _createdDatasetId = await _facade.SaveStudentDatasetAsync(name, students, _importedFileName);
            }
            else
            {
                await _facade.UpdateStudentDatasetAsync(_createdDatasetId, name, students, _importedFileName);
            }

            DatasetName = name;
            SelectedDatasetId = _createdDatasetId;
            _loadedExistingDatasetId = null;
            _loadedDatasetName = null;
            SelectedExistingDataset = null;
            _rosterDirty = false;
            RefreshState();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OOBE 保存名单失败");
            RosterError = ex.Message;
            return false;
        }
    }

    private async Task LoadDatasetsAsync()
    {
        try
        {
            var datasets = await _facade.ListStudentDatasetsAsync();
            ExistingDatasets = new ObservableCollection<StudentDatasetInfo>(
                datasets.OrderBy(d => d.Name, StringComparer.CurrentCulture));

            if (_loadedExistingDatasetId is not null)
            {
                var loaded = ExistingDatasets.FirstOrDefault(d => d.Id == _loadedExistingDatasetId);
                if (loaded is null)
                {
                    // 数据集已被外部删除：回到可编辑状态，保留当前行以便重新保存。
                    _loadedExistingDatasetId = null;
                    _loadedDatasetName = null;
                    SelectedExistingDataset = null;
                }
                else
                {
                    SelectedExistingDataset = loaded;
                }
            }
            else if (SelectedExistingDataset is { } pending)
            {
                SelectedExistingDataset = ExistingDatasets.FirstOrDefault(d => d.Id == pending.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OOBE 加载已有名单失败");
            ErrorMessage = ex.Message;
        }
    }

    private void SetRosterRows(IEnumerable<Student> students)
    {
        _suppressRosterTracking = true;
        try
        {
            if (!_rosterCollectionSubscribed)
            {
                RosterRows.CollectionChanged += OnRosterRowsChanged;
                _rosterCollectionSubscribed = true;
            }

            // Clear() 触发 Reset（OldItems 为空），需手动解除旧行订阅。
            foreach (var row in RosterRows)
                row.PropertyChanged -= OnRosterRowPropertyChanged;

            RosterRows.Clear();
            foreach (var student in students)
                RosterRows.Add(new StudentRowViewModel(student));
        }
        finally
        {
            _suppressRosterTracking = false;
        }

        _rosterDirty = false;
        NewStudent = new Student();
        UseVirtualization = RosterRows.Count > 300;
        RefreshState();
    }

    private void OnRosterRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (StudentRowViewModel row in e.OldItems)
                row.PropertyChanged -= OnRosterRowPropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (StudentRowViewModel row in e.NewItems)
                row.PropertyChanged += OnRosterRowPropertyChanged;
        }

        if (_suppressRosterTracking)
            return;

        UseVirtualization = RosterRows.Count > 300;
        _rosterDirty = true;
        RefreshState();
    }

    private void OnRosterRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudentRowViewModel.IsEditing))
            return;

        if (!_suppressRosterTracking)
            _rosterDirty = true;
        RefreshState();
    }

    // ── 会场 ──

    private void ResetVenueDefaults()
    {
        _suppressVenueTracking = true;
        try
        {
            Rows = DefaultRows;
            Columns = DefaultColumns;
            AisleWidth = DefaultAisleWidth;
            _createdVenueId = Guid.NewGuid().ToString("N")[..8];
            VenueName = string.Format(Resources.Venue_NewVenueFmt, _createdVenueId);
            IsExistingVenueReadOnly = false;
            IsEditingExistingVenue = false;
            _seatsPerDesk = VenueLayoutDefaults.GridSeatsPerDesk;
        }
        finally
        {
            _suppressVenueTracking = false;
        }

        RebuildAisleOptions([], []);
        RebuildColumnRowCountOptions([]);
        PreviewRevision++;
    }

    private async Task LoadVenuesAsync()
    {
        try
        {
            var venues = await _facade.ListVenueSummariesAsync();
            ExistingVenues = new ObservableCollection<VenueSummary>(
                venues.OrderBy(v => v.Name, StringComparer.CurrentCulture));

            if (_loadedVenueId is not null)
            {
                var loaded = ExistingVenues.FirstOrDefault(v => v.Id == _loadedVenueId);
                if (loaded is null)
                {
                    // 会场已被外部删除：回到新建状态，避免沿用失效 id。
                    ClearLoadedVenue();
                }
                else
                {
                    _loadedVenueSummary = loaded;
                    SelectedExistingVenue = loaded;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OOBE 加载已有会场失败");
            ErrorMessage = ex.Message;
        }
    }

    private void ClearLoadedVenue()
    {
        _loadedVenueId = null;
        _loadedVenueSummary = null;
        _editingVenueId = null;
        _createdVenueId = null;
        _createdGridSeatMap = null;
        _existingVenueBaseMetadata = null;
        _existingGridSeatMap = null;
        _existingVenueObstacles = null;
        SelectedExistingVenue = null;
        ResetVenueDefaults();
        _venueDirty = false;
        PreviewRevision++;
        RefreshState();
    }

    private void StartNewVenue()
    {
        ErrorMessage = null;
        VenueError = null;
        _createdVenueId = null;
        _createdGridSeatMap = null;
        _editingVenueId = null;
        _loadedVenueId = null;
        _loadedVenueSummary = null;
        _existingVenueBaseMetadata = null;
        _existingGridSeatMap = null;
        _existingVenueObstacles = null;
        SelectedExistingVenue = null;
        ResetVenueDefaults();
        _venueDirty = false;
        PreviewRevision++;
        RefreshState();
    }

    private async Task SelectExistingVenueAsync(VenueSummary? summary)
    {
        if (summary is null || IsVenueLoading) return;

        ErrorMessage = null;
        VenueError = null;
        IsVenueLoading = true;
        RefreshState();
        try
        {
            var layout = await _facade.LoadVenueAsync(summary.Id);
            if (layout is null)
            {
                VenueError = Resources.Oobe_ErrorVenueLoad;
                RevertVenueSelection();
                return;
            }

            _suppressVenueTracking = true;
            try
            {
                SelectedExistingVenue = summary;
                _loadedVenueId = summary.Id;
                _loadedVenueSummary = summary;
                _editingVenueId = summary.Id;
                _createdVenueId = null;
                _createdGridSeatMap = null;
                IsEditingExistingVenue = true;
                VenueName = layout.Name;
                _existingVenueObstacles = layout.Obstacles
                    .Select(CloneObstacle)
                    .ToList();

                if (layout.Metadata is GridLayoutMetadata grid)
                {
                    IsExistingVenueReadOnly = false;
                    _existingVenueBaseMetadata = grid;
                    _existingGridSeatMap = layout.Seats
                        .OfType<GridSeat>()
                        .ToDictionary(s => (s.Row, s.Column), s => s.Id);

                    Rows = grid.Rows is >= 1 and <= MaxGridSize ? grid.Rows : DefaultRows;
                    Columns = grid.Columns is >= 1 and <= MaxGridSize ? grid.Columns : DefaultColumns;
                    AisleWidth = (int)Math.Round(grid.AisleWidth > 0 ? grid.AisleWidth : DefaultAisleWidth);
                    _seatsPerDesk = grid.SeatsPerDesk is >= 1 and <= 6 ? grid.SeatsPerDesk : VenueLayoutDefaults.GridSeatsPerDesk;

                    RebuildAisleOptions(grid.AisleAfterColumns, grid.AisleAfterRows);
                    RebuildColumnRowCountOptions(
                        grid.ColumnRowCounts.Select(v => (int?)v).ToList());
                }
                else
                {
                    IsExistingVenueReadOnly = true;
                    _existingVenueBaseMetadata = null;
                    _existingGridSeatMap = null;
                    PreviewSnapshot = null;
                    PreviewSeatCount = 0;
                }
            }
            finally
            {
                _suppressVenueTracking = false;
            }

            _venueDirty = false;
            PreviewRevision++;
            RefreshState();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OOBE 加载已有会场失败");
            VenueError = ex.Message;
            RevertVenueSelection();
        }
        finally
        {
            IsVenueLoading = false;
            RefreshState();
        }
    }

    private void RevertVenueSelection()
    {
        SelectedExistingVenue = _loadedVenueSummary;
    }

    private async Task<bool> SaveVenueAsync()
    {
        VenueError = null;

        if (IsVenueLoading) return false;

        if (IsVenueSelectionPending)
        {
            VenueError = Resources.Oobe_ErrorVenueNotLoaded;
            return false;
        }

        if (IsExistingVenueReadOnly)
        {
            if (_loadedVenueId is null)
            {
                VenueError = Resources.Oobe_ErrorVenueLoad;
                return false;
            }

            return true;
        }

        if (_editingVenueId is not null && !_venueDirty)
            return true;

        if (string.IsNullOrWhiteSpace(VenueName))
        {
            VenueError = Resources.Oobe_ErrorVenueName;
            return false;
        }

        if (Rows is < 1 or > MaxGridSize)
        {
            VenueError = Resources.Oobe_ErrorVenueRows;
            return false;
        }

        if (Columns is < 1 or > MaxGridSize)
        {
            VenueError = Resources.Oobe_ErrorVenueColumns;
            return false;
        }

        var isExisting = _editingVenueId is not null;
        var venueId = isExisting
            ? _editingVenueId!
            : (_createdVenueId ??= Guid.NewGuid().ToString("N")[..8]);

        try
        {
            var meta = BuildGridMetadata(isExisting ? _existingVenueBaseMetadata : null);
            var layout = GridLayoutBuilder.BuildGrid(meta);
            layout.Id = venueId;
            layout.Name = VenueName.Trim();

            if (isExisting && _existingVenueObstacles is { } obstacles)
                layout.Obstacles = obstacles.Select(CloneObstacle).ToList();

            var seatMap = isExisting ? _existingGridSeatMap : _createdGridSeatMap;
            if (seatMap is { Count: > 0 })
            {
                foreach (var seat in layout.Seats.OfType<GridSeat>())
                {
                    if (seatMap.TryGetValue((seat.Row, seat.Column), out var oldId))
                        seat.Id = oldId;
                }
            }

            var editRevision = _venueEditRevision;
            await _facade.SaveVenueAsync(venueId, layout);
            _loadedVenueId = venueId;
            if (isExisting)
            {
                _existingVenueBaseMetadata = meta;
                _existingGridSeatMap = layout.Seats
                    .OfType<GridSeat>()
                    .ToDictionary(s => (s.Row, s.Column), s => s.Id);
            }
            else
            {
                _createdGridSeatMap = layout.Seats
                    .OfType<GridSeat>()
                    .ToDictionary(s => (s.Row, s.Column), s => s.Id);
            }

            // 保存期间用户仍可能继续编辑；只有编辑版本没变才清除脏标记并允许推进。
            if (_venueEditRevision != editRevision)
            {
                _venueDirty = true;
                VenueError = Resources.Oobe_ErrorVenueChanged;
                RefreshState();
                return false;
            }

            _venueDirty = false;
            RefreshState();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OOBE 保存会场失败");
            VenueError = ex.Message;
            return false;
        }
    }

    private static Obstacle CloneObstacle(Obstacle obstacle) => new()
    {
        Id = obstacle.Id,
        X = obstacle.X,
        Y = obstacle.Y,
        Width = obstacle.Width,
        Height = obstacle.Height,
        Type = obstacle.Type,
    };

    private void RebuildAisleOptions(
        IEnumerable<int>? selectedColumns = null,
        IEnumerable<int>? selectedRows = null)
    {
        var columnSelection = new HashSet<int>(
            selectedColumns ?? AisleColumnOptions.Where(o => o.IsSelected).Select(o => o.SeatColumn));
        var rowSelection = new HashSet<int>(
            selectedRows ?? AisleRowOptions.Where(o => o.IsSelected).Select(o => o.SeatColumn));

        int deskColumns = Columns / Math.Max(1, _seatsPerDesk);

        var columns = new ObservableCollection<AisleOption>();
        for (int d = 1; d < deskColumns; d++)
        {
            int seatColumn = d * _seatsPerDesk;
            var option = new AisleOption(
                string.Format(Resources.Venue_ColAisleFmt, seatColumn),
                seatColumn,
                string.Format(Resources.Venue_ColAisleTooltipFmt, seatColumn, seatColumn + 1),
                columnSelection.Contains(seatColumn));
            option.PropertyChanged += OnVenueOptionChanged;
            columns.Add(option);
        }

        var rows = new ObservableCollection<AisleOption>();
        for (int r = 1; r < Rows; r++)
        {
            var option = new AisleOption(
                string.Format(Resources.Venue_RowAisleFmt, r),
                r,
                string.Format(Resources.Venue_RowAisleTooltipFmt, r, r + 1),
                rowSelection.Contains(r));
            option.PropertyChanged += OnVenueOptionChanged;
            rows.Add(option);
        }

        AisleColumnOptions = columns;
        AisleRowOptions = rows;
    }

    private void RebuildColumnRowCountOptions(IReadOnlyList<int?>? values = null)
    {
        var source = values ?? ColumnRowCountOptions.Select(o => o.Rows).ToList();
        int target = Math.Max(1, Columns);
        var options = new ObservableCollection<ColumnRowCountOption>();

        for (int i = 0; i < target; i++)
        {
            var option = new ColumnRowCountOption(i + 1)
            {
                Rows = i < source.Count && source[i] is > 0 ? source[i] : null,
            };
            option.PropertyChanged += OnVenueOptionChanged;
            options.Add(option);
        }

        ColumnRowCountOptions = options;
    }

    private void OnVenueOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressVenueTracking) return;
        _venueDirty = true;
        _venueEditRevision++;
        PreviewRevision++;
        RefreshState();
    }

    private void RecomputePreviewNow()
    {
        if (IsExistingVenueReadOnly)
        {
            PreviewSnapshot = null;
            PreviewSeatCount = 0;
            return;
        }

        try
        {
            var meta = BuildGridMetadata(_editingVenueId is not null ? _existingVenueBaseMetadata : null);
            var overlays = _editingVenueId is not null && _existingVenueObstacles is { Count: > 0 }
                ? BuildObstacleOverlays(_existingVenueObstacles)
                : null;
            var snapshot = VenueGridPreviewBuilder.Build(meta, null, overlays);
            PreviewSnapshot = snapshot;
            PreviewSeatCount = snapshot.Seats.Count(s => !s.IsDisabled);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OOBE 会场预览重算失败");
            PreviewSnapshot = SeatLayoutSnapshot.Empty;
            PreviewSeatCount = 0;
        }
    }

    /// <summary>已有会场的门/柱子等障碍物转为预览覆盖物（讲台由元数据单独绘制，避免重复）。</summary>
    private static List<BoardOverlay> BuildObstacleOverlays(IEnumerable<Obstacle> obstacles)
    {
        var overlays = new List<BoardOverlay>();
        foreach (var obstacle in obstacles)
        {
            if (string.Equals(obstacle.Type, "Podium", StringComparison.OrdinalIgnoreCase))
                continue;

            bool isDoor = string.Equals(obstacle.Type, "Door", StringComparison.OrdinalIgnoreCase);
            overlays.Add(new BoardOverlay(
                obstacle.X,
                obstacle.Y,
                obstacle.Width > 0 ? obstacle.Width : 36,
                obstacle.Height > 0 ? obstacle.Height : 24,
                isDoor ? Resources.Freeform_Door : obstacle.Type,
                IsDoor: isDoor));
        }

        return overlays;
    }

    private GridLayoutMetadata BuildGridMetadata(GridLayoutMetadata? baseMetadata)
    {
        var columnRowCounts = BuildColumnRowCounts();

        GridLayoutMetadata meta;
        if (baseMetadata is null)
        {
            meta = new GridLayoutMetadata
            {
                HorizontalSpacing = VenueLayoutDefaults.GridHorizontalSpacing,
                VerticalSpacing = VenueLayoutDefaults.GridVerticalSpacing,
                OriginX = VenueLayoutDefaults.GridOriginX,
                OriginY = VenueLayoutDefaults.GridOriginY,
                SeatsPerDesk = VenueLayoutDefaults.GridSeatsPerDesk,
                IntraDeskSpacing = VenueLayoutDefaults.GridIntraDeskSpacing,
                InterDeskSpacing = VenueLayoutDefaults.GridInterDeskSpacing,
                FrontRowCount = VenueLayoutDefaults.GridFrontRowCount,
                HasPodium = VenueLayoutDefaults.GridHasPodium,
                PodiumWidth = VenueLayoutDefaults.GridPodiumWidth,
                PodiumHeight = VenueLayoutDefaults.GridPodiumHeight,
            };
        }
        else
        {
            meta = GridVisualGeometryBuilder.Clone(baseMetadata);
            meta.EmptyPositions = FilterEmptyPositions(baseMetadata.EmptyPositions, columnRowCounts);
        }

        meta.Rows = Rows;
        meta.Columns = Columns;
        meta.AisleWidth = AisleWidth;
        meta.AisleAfterColumns = AisleColumnOptions
            .Where(o => o.IsSelected)
            .Select(o => o.SeatColumn)
            .OrderBy(v => v)
            .ToList();
        meta.AisleAfterRows = AisleRowOptions
            .Where(o => o.IsSelected)
            .Select(o => o.SeatColumn)
            .OrderBy(v => v)
            .ToList();
        meta.ColumnRowCounts = columnRowCounts;
        return meta;
    }

    private List<int> BuildColumnRowCounts()
    {
        int last = -1;
        for (int i = 0; i < ColumnRowCountOptions.Count; i++)
        {
            if (ColumnRowCountOptions[i].Rows is > 0) last = i;
        }

        if (last < 0) return [];

        var result = new List<int>(last + 1);
        for (int i = 0; i <= last; i++)
        {
            int? value = ColumnRowCountOptions[i].Rows;
            result.Add(value is > 0 ? value.Value : Rows);
        }

        return result;
    }

    private List<GridPosition> FilterEmptyPositions(
        IReadOnlyList<GridPosition>? positions,
        IReadOnlyList<int> columnRowCounts)
    {
        if (positions is null or { Count: 0 }) return [];

        return positions
            .Where(p => p.Row >= 1 && p.Column >= 1)
            .Where(p => p.Column <= Columns && p.Row <= Rows)
            .Where(p => p.Row <= (p.Column <= columnRowCounts.Count
                ? columnRowCounts[p.Column - 1]
                : int.MaxValue))
            .ToList();
    }

    // ── 交接 ──

    private async Task StartHandoffAsync()
    {
        ErrorMessage = null;

        var seating = _navigation.CurrentViewModel as SeatingArrangementViewModel;
        if (_navigation.CurrentPage != PageKey.SeatingArrangement)
        {
            var navigated = await _navigation.NavigateToAsync(PageKey.SeatingArrangement);
            if (!navigated && _navigation.CurrentPage != PageKey.SeatingArrangement)
            {
                ErrorMessage = Resources.Oobe_ErrorHandoffNavigation;
                return;
            }

            seating = _navigation.CurrentViewModel as SeatingArrangementViewModel;
        }

        if (seating is null)
        {
            ErrorMessage = Resources.Oobe_ErrorHandoffPrepare;
            return;
        }

        // 先订阅再准备：准备阶段超时/异常后用户仍可能点击生成，
        // 成功时仍需切换到成功卡并落盘 IsFirstLaunch=false。
        DetachHandoff();
        seating.SuppressGenerationErrorDialog = true;
        _handoffSeating = seating;
        _handoffSeating.SeatingGenerationCompleted += OnSeatingGenerationCompleted;

        try
        {
            var preparation = await seating.PrepareOobeHandoffAsync(_loadedVenueId, SelectedDatasetId);
            _missingVenue = !preparation.VenueReady;
            _missingDataset = !preparation.DatasetReady;

            IsVisible = false;
            IsHandoffVisible = true;
            SetHandoffState(preparation.IsReady ? OobeHandoffState.Prompt : OobeHandoffState.Incomplete);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OOBE 交接准备失败");
            ErrorMessage = Resources.Oobe_ErrorHandoffPrepare;
            IsVisible = false;
            IsHandoffVisible = true;
            _missingVenue = true;
            _missingDataset = true;
            SetHandoffState(OobeHandoffState.Incomplete);
        }

        RefreshState();
    }

    private void OnSeatingGenerationCompleted(object? sender, SeatingGenerationResult result)
    {
        if (_firstSuccessConsumed) return;

        switch (result.Status)
        {
            case SeatingGenerationStatus.Success:
                _firstSuccessConsumed = true;
                _ = MarkFirstLaunchCompletedSafeAsync();
                SetHandoffState(OobeHandoffState.Success);
                break;

            case SeatingGenerationStatus.Failed:
                SetHandoffState(OobeHandoffState.Failure, result.Message);
                break;

            case SeatingGenerationStatus.MissingData:
                _missingVenue = result.MissingData is SeatingMissingData.Venue or SeatingMissingData.Both;
                _missingDataset = result.MissingData is SeatingMissingData.Dataset or SeatingMissingData.Both;
                if (!_missingVenue && !_missingDataset)
                {
                    // 兼容未携带缺失详情的旧结果：回退到交接页当前状态。
                    var seating = _handoffSeating;
                    _missingVenue = seating is null || !seating.HasSelectedVenue || !seating.HasLayoutForGeneration;
                    _missingDataset = seating is null || !seating.HasSelectedDataset;
                }
                SetHandoffState(OobeHandoffState.Incomplete, result.Message);
                break;
        }
    }

    private async Task MarkFirstLaunchCompletedSafeAsync()
    {
        if (!await TryMarkFirstLaunchCompletedAsync())
            _logger.LogWarning("[OOBE] 首次生成成功后仍无法落盘 IsFirstLaunch=false");
    }

    private async Task<bool> TryMarkFirstLaunchCompletedAsync()
    {
        try
        {
            var settings = await _facade.LoadAppSettingsAsync();
            if (!settings.IsFirstLaunch) return true;

            settings.IsFirstLaunch = false;
            await _facade.SaveAppSettingsAsync(settings);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[OOBE] 落盘 IsFirstLaunch=false 失败");
            return false;
        }
    }

    private Task HandoffPrimaryAsync() => RunBusyAsync(async () =>
    {
        switch (HandoffState)
        {
            case OobeHandoffState.Success:
                await CompleteAsync(skipped: false);
                break;

            case OobeHandoffState.Incomplete:
                await GoToMissingStepAsync();
                break;
        }
    });

    private Task HandoffSecondaryAsync() => RunBusyAsync(async () =>
    {
        if (HandoffState is OobeHandoffState.Prompt
            or OobeHandoffState.Failure
            or OobeHandoffState.Incomplete)
        {
            await CompleteAsync(skipped: true);
        }
    });

    private async Task GoToMissingStepAsync()
    {
        IsHandoffVisible = false;
        IsVisible = true;
        DetachHandoff();
        var step = _missingDataset ? OobeStep.Roster : OobeStep.Venue;
        await GoToStepAsync(step);
    }

    private void OnNavigationChanged()
    {
        if (!IsHandoffVisible) return;
        if (_navigation.CurrentPage == PageKey.SeatingArrangement) return;
        _ = CompleteAsync(skipped: true);
    }

    private void SetHandoffState(OobeHandoffState state, string? reason = null)
    {
        HandoffState = state;
        switch (state)
        {
            case OobeHandoffState.Prompt:
                HandoffTitle = Resources.Oobe_HandoffPromptTitle;
                HandoffMessage = Resources.Oobe_HandoffPromptBody;
                HandoffPrimaryText = string.Empty;
                HandoffSecondaryText = Resources.Oobe_HandoffSkip;
                break;

            case OobeHandoffState.Success:
                HandoffTitle = Resources.Oobe_HandoffSuccessTitle;
                HandoffMessage = Resources.Oobe_HandoffSuccessBody;
                HandoffPrimaryText = Resources.Oobe_HandoffFinish;
                HandoffSecondaryText = string.Empty;
                break;

            case OobeHandoffState.Failure:
                HandoffTitle = Resources.Oobe_HandoffFailureTitle;
                HandoffMessage = string.IsNullOrWhiteSpace(reason)
                    ? Resources.Oobe_HandoffFailureBody
                    : reason;
                HandoffPrimaryText = string.Empty;
                HandoffSecondaryText = Resources.Oobe_HandoffSkip;
                break;

            case OobeHandoffState.Incomplete:
                HandoffTitle = Resources.Oobe_HandoffIncompleteTitle;
                HandoffMessage = string.Format(
                    Resources.Oobe_HandoffIncompleteBody,
                    MissingLabel());
                HandoffPrimaryText = Resources.Oobe_HandoffGoComplete;
                HandoffSecondaryText = Resources.Oobe_HandoffFinishSetup;
                break;

            default:
                HandoffTitle = string.Empty;
                HandoffMessage = string.Empty;
                HandoffPrimaryText = string.Empty;
                HandoffSecondaryText = string.Empty;
                break;
        }

        RefreshState();
    }

    private string MissingLabel()
    {
        if (_missingVenue && _missingDataset) return Resources.Oobe_HandoffMissingBoth;
        return _missingVenue
            ? Resources.Oobe_HandoffMissingVenue
            : Resources.Oobe_HandoffMissingRoster;
    }

    private void DetachHandoff()
    {
        if (_handoffSeating is null) return;
        _handoffSeating.SeatingGenerationCompleted -= OnSeatingGenerationCompleted;
        _handoffSeating.SuppressGenerationErrorDialog = false;
        _handoffSeating = null;
    }

    // ── 设置持久化 ──

    private async Task<bool> SaveSettingsAsync(Action<AppSettings> mutate, string errorMessage)
    {
        try
        {
            var settings = await _facade.LoadAppSettingsAsync();
            mutate(settings);
            await _facade.SaveAppSettingsAsync(settings);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OOBE 保存设置失败");
            ErrorMessage = errorMessage;
            return false;
        }
    }

    // ── 状态通知 ──

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsFullScreenVisible));
        OnPropertyChanged(nameof(IsWelcomeStep));
        OnPropertyChanged(nameof(IsPreferencesStep));
        OnPropertyChanged(nameof(IsRosterStep));
        OnPropertyChanged(nameof(IsVenueStep));
        OnPropertyChanged(nameof(PreviousStep));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(HasRoster));
        OnPropertyChanged(nameof(IsRosterSelectionPending));
        OnPropertyChanged(nameof(IsVenueSelectionPending));
        OnPropertyChanged(nameof(CanSaveVenue));
        OnPropertyChanged(nameof(CanProceed));
        OnPropertyChanged(nameof(IsVenuePreviewVisible));
        OnPropertyChanged(nameof(RosterPrimaryText));
        OnPropertyChanged(nameof(VenuePrimaryText));
        OnPropertyChanged(nameof(PreviewSeatCountText));
        OnPropertyChanged(nameof(IsHandoffPrompt));
        OnPropertyChanged(nameof(IsHandoffSuccess));
        OnPropertyChanged(nameof(IsHandoffFailure));
        OnPropertyChanged(nameof(IsHandoffIncomplete));
        OnPropertyChanged(nameof(RestoredHint));
    }

    partial void OnStepChanged(OobeStep value) => RefreshState();

    partial void OnIsBusyChanged(bool value) => RefreshState();

    partial void OnIsVisibleChanged(bool value) => RefreshState();

    partial void OnIsHandoffVisibleChanged(bool value) => RefreshState();

    partial void OnIsRosterLoadingChanged(bool value) => RefreshState();

    partial void OnIsVenueLoadingChanged(bool value) => RefreshState();

    partial void OnSelectedExistingDatasetChanged(StudentDatasetInfo? value) => RefreshState();

    partial void OnDatasetNameChanged(string value)
    {
        if (_suppressRosterTracking || _loadedExistingDatasetId is null) return;
        if (!string.Equals(value, _loadedDatasetName, StringComparison.Ordinal))
            _rosterDirty = true;
    }

    partial void OnSelectedExistingVenueChanged(VenueSummary? value) => RefreshState();

    partial void OnIsExistingVenueReadOnlyChanged(bool value) => RefreshState();

    partial void OnIsEditingExistingVenueChanged(bool value) => RefreshState();

    partial void OnRowsChanged(int value)
    {
        if (_suppressVenueTracking) return;
        _venueDirty = true;
        _venueEditRevision++;
        RebuildAisleOptions();
        RebuildColumnRowCountOptions();
        PreviewRevision++;
        RefreshState();
    }

    partial void OnColumnsChanged(int value)
    {
        if (_suppressVenueTracking) return;
        _venueDirty = true;
        _venueEditRevision++;
        RebuildAisleOptions();
        RebuildColumnRowCountOptions();
        PreviewRevision++;
        RefreshState();
    }

    partial void OnAisleWidthChanged(int value)
    {
        if (_suppressVenueTracking) return;
        _venueDirty = true;
        _venueEditRevision++;
        PreviewRevision++;
        RefreshState();
    }

    partial void OnVenueNameChanged(string value)
    {
        if (_suppressVenueTracking) return;
        _venueDirty = true;
        _venueEditRevision++;
        RefreshState();
    }

    partial void OnHandoffStateChanged(OobeHandoffState value) => RefreshState();

    partial void OnPreviewSeatCountChanged(int value)
        => OnPropertyChanged(nameof(PreviewSeatCountText));

    // ── IStudentRosterEditor（共享名单表格） ──

    ICommand IStudentRosterEditor.BeginEditCommand => BeginEditCommand;

    ICommand IStudentRosterEditor.EndEditCommand => EndEditCommand;

    ICommand IStudentRosterEditor.CancelEditCommand => CancelEditCommand;

    ICommand IStudentRosterEditor.DeleteStudentCommand => DeleteStudentCommand;

    ICommand IStudentRosterEditor.AddNewStudentCommand => AddNewStudentCommand;
}
