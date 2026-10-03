using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Application.Commands;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.DomainServices;
using SeatFlow.Core.Models;
using SeatFlow.Core.Strategies;
using SeatFlow.Core.Workspace;
using SeatFlow.Infrastructure.Serialization;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.Helpers;
using SeatFlow.Presentation.Avalonia.Services;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

public partial class SeatingArrangementViewModel : ViewModelBase, IPageLifecycle, IFileDropHandler, IGuideSeedTarget
{
    private readonly IApplicationFacade _facade;
    private readonly IFileService _fileService;
    private readonly IArrangementCounterService _counterService;
    private readonly IShellLayoutService _layout;
    private readonly IServiceProvider _services;
    /// <summary>当前平台是否浏览器（WASM）：Web 版隐藏 PDF/图片导出。</summary>
    public bool IsWebPlatform => OperatingSystem.IsBrowser();

    private readonly ILogger<SeatingArrangementViewModel> _logger;

    // ── 内部状态 ──
    private SeatingWorkspace? _workspace;
    private ClassroomLayoutDefinition? _currentLayout;
    private SeatingPlan? _currentPlan;
    private SeatDisplayItem? _swapSourceSeat;
    private CancellationTokenSource? _generateCts;
    private CancellationTokenSource? _enterCts;

    // ── 操作历史 ──
    private readonly ObservableCollection<HistoryEntry> _historyEntries = [];
    private int _currentHistoryIndex = -1;
    private int _lastSavedIndex = -1;
    public IReadOnlyList<HistoryEntry> HistoryEntries => _historyEntries;
    public bool HasUnsavedChanges => _currentHistoryIndex >= 0 && _currentHistoryIndex != _lastSavedIndex;
    public bool HasHistory => _historyEntries.Count > 0;

    // ── 左侧面板 ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVenue))]
    public partial ObservableCollection<VenueItem> VenueItems { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVenue))]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanCreateEmpty))]
    public partial VenueItem? SelectedVenue { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDataset))]
    public partial ObservableCollection<StudentDatasetInfo> DatasetItems { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDataset))]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanCreateEmpty))]
    public partial StudentDatasetInfo? SelectedDataset { get; set; }

    public bool HasSelectedVenue => SelectedVenue != null;
    public bool HasSelectedDataset => SelectedDataset != null;

    // ── Canvas ──
    [ObservableProperty]
    public partial ObservableCollection<SeatDisplayItem> SeatItems { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<SeatDisplayItem> OverlayItems { get; set; } = [];

    [ObservableProperty]
    public partial double CanvasWidth { get; set; } = 800;

    [ObservableProperty]
    public partial double CanvasHeight { get; set; } = 600;

    /// <summary>自绘画布渲染快照（不可变；引用替换触发重绘）。</summary>
    [ObservableProperty]
    public partial SeatLayoutSnapshot? CanvasSnapshot { get; set; }

    /// <summary>画布缩放（双向绑定到 SeatingCanvas.Zoom；缩放不再重建座位集合）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomPercentDisplay))]
    public partial double ZoomLevel { get; set; } = 1.0;

    /// <summary>缩放百分比显示（控制条，如「120%」）。</summary>
    public string ZoomPercentDisplay => $"{Math.Round(ZoomLevel * 100)}%";

    /// <summary>设置中的座位图默认缩放（控制条点按百分比时重置目标）。</summary>
    public double DefaultZoomLevel => _defaultZoomLevel;

    private double _defaultZoomLevel = 1.0;
    private int _snapshotVersion;

    /// <summary>不改变数据，仅重新绘制预览区域。</summary>
    public void RefreshPreview() => BuildSeatDisplayItems();

    // ── 工具栏 ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanCreateEmpty))]
    public partial bool IsGenerating { get; set; }

    [ObservableProperty]
    public partial bool HasGenerated { get; set; }


    public bool CanGenerate => HasSelectedVenue && HasSelectedDataset && !IsGenerating;
    public bool CanCreateEmpty => HasSelectedVenue && HasSelectedDataset && !IsGenerating;
    public bool CanUndo => _currentHistoryIndex > 0;
    public bool CanRedo => _currentHistoryIndex < _historyEntries.Count - 1;

    // ── 右侧面板 ──
    [ObservableProperty]
    public partial ObservableCollection<StrategyDisplayInfo> ActiveStrategies { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<Student> UnassignedStudents { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedUnassignedStudent))]
    public partial Student? SelectedUnassignedStudent { get; set; }

    public bool HasSelectedUnassignedStudent => SelectedUnassignedStudent != null;

    [ObservableProperty]
    public partial ObservableCollection<StrategyMessageGroup> MessageGroups { get; set; } = [];

    public bool HasMessages => MessageGroups.Count > 0;

    // ── M3 工作台外壳：紧凑断点 / 抽屉 / 欢迎卡 ──

    /// <summary>外壳布局状态（紧凑模式下左选择栏与右检查器转为右抽屉）。</summary>
    public IShellLayoutService Layout => _layout;

    /// <summary>空态欢迎卡（承接原 Home 的欢迎语 / 快捷链接 / RELEASE 更新说明）。</summary>
    public WelcomeCardViewModel Welcome { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickerVisible))]
    [NotifyPropertyChangedFor(nameof(DrawerMaskVisible))]
    public partial bool IsPickerDrawerOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InspectorVisible))]
    [NotifyPropertyChangedFor(nameof(DrawerMaskVisible))]
    public partial bool IsInspectorDrawerOpen { get; set; }

    /// <summary>桌面：左选择栏常驻；紧凑：仅抽屉展开时可见。</summary>
    public bool PickerVisible => !_layout.IsCompact || IsPickerDrawerOpen;

    /// <summary>左选择栏宽度（桌面内联 248；紧凑抽屉 320）。</summary>
    public double PickerPanelWidth => _layout.IsCompact ? 320 : 248;

    /// <summary>紧凑模式隐藏命令栏标题/副标题/按钮文字（避免溢出重叠）。</summary>
    public bool CompactCommandTextVisible => !_layout.IsCompact;

    /// <summary>左选择栏所在列（桌面 0；紧凑切到画布列做右侧抽屉覆盖）。</summary>
    public int PickerPanelColumn => _layout.IsCompact ? 1 : 0;

    /// <summary>右检查器所在列（桌面 2；紧凑切到画布列做右侧抽屉覆盖）。</summary>
    public int InspectorPanelColumn => _layout.IsCompact ? 1 : 2;

    /// <summary>抽屉/内联面板的水平对齐（紧凑右对齐，桌面拉伸）。</summary>
    public global::Avalonia.Layout.HorizontalAlignment PanelHorizontalAlignment =>
        _layout.IsCompact ? global::Avalonia.Layout.HorizontalAlignment.Right : global::Avalonia.Layout.HorizontalAlignment.Stretch;

    /// <summary>面板层级（紧凑抽屉需在遮罩之上）。</summary>
    public int PanelZIndex => _layout.IsCompact ? 30 : 0;

    /// <summary>左选择栏描边（桌面右边框；紧凑抽屉左边框）。</summary>
    public global::Avalonia.Thickness PickerPanelBorderThickness =>
        _layout.IsCompact ? new global::Avalonia.Thickness(1, 0, 0, 0) : new global::Avalonia.Thickness(0, 0, 1, 0);

    /// <summary>桌面：右检查器常驻；紧凑：仅抽屉展开时可见。</summary>
    public bool InspectorVisible => !_layout.IsCompact || IsInspectorDrawerOpen;

    /// <summary>紧凑模式下任一抽屉展开时显示遮罩。</summary>
    public bool DrawerMaskVisible => _layout.IsCompact && (IsPickerDrawerOpen || IsInspectorDrawerOpen);

    /// <summary>命令栏副标题：当前会场 · 当前名单。</summary>
    public string WorkbenchSubtitle =>
        $"{SelectedVenue?.Name ?? Resources.Seating_Venue} · {SelectedDataset?.Name ?? Resources.Seating_MemberData}";

    // ── 状态栏 ──
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = Resources.Seating_Ready;

    [ObservableProperty]
    public partial int TotalSeats { get; set; }

    [ObservableProperty]
    public partial int AssignedSeats { get; set; }

    public int UnassignedStudentCount => UnassignedStudents.Count;

    // ── 交换模式 ──
    [ObservableProperty]
    public partial bool IsSwapMode { get; set; }

    [ObservableProperty]
    public partial string SwapHintText { get; set; } = string.Empty;

    /// <summary>
    /// 本次进入页面的加载完成信号：<see cref="OnLeaveAsync"/> 时替换为新的未完成任务，
    /// <see cref="OnEnterAsync"/> 内部的刷新结束后置位。
    /// 引导示例数据注入（OnboardingService.SeedSeatingArrangementData）需等待它，
    /// 否则随后加载会整体替换 VenueItems/DatasetItems、覆盖演示数据。
    /// 初始为已完成，兼容「构造后从未进入过页面」的读取场景。
    /// </summary>
    public Task InitializationTask => _enterCompletion.Task;

    private TaskCompletionSource _enterCompletion = CreateCompletedCompletion();

    private static TaskCompletionSource CreateCompletedCompletion()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult();
        return tcs;
    }

    public SeatingArrangementViewModel(
        IApplicationFacade facade,
        IFileService fileService,
        IArrangementCounterService counterService,
        IShellLayoutService layout,
        WelcomeCardViewModel welcome,
        IServiceProvider services,
        IDialogService dialog,
        ILogger<SeatingArrangementViewModel>? logger = null) : base(dialog, logger)
    {
        _facade = facade;
        _fileService = fileService;
        _counterService = counterService;
        _layout = layout;
        Welcome = welcome;
        _services = services;
        _logger = logger ?? NullLogger<SeatingArrangementViewModel>.Instance;
        // 紧凑断点变化：抽屉互斥/可见性联动
        _layout.PropertyChanged += OnLayoutChanged;
    }

    private void OnLayoutChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IShellLayoutService.IsCompact)) return;
        if (!_layout.IsCompact)
        {
            IsPickerDrawerOpen = false;
            IsInspectorDrawerOpen = false;
        }
        OnPropertyChanged(nameof(PickerVisible));
        OnPropertyChanged(nameof(PickerPanelWidth));
        OnPropertyChanged(nameof(InspectorVisible));
        OnPropertyChanged(nameof(DrawerMaskVisible));
        OnPropertyChanged(nameof(CompactCommandTextVisible));
        OnPropertyChanged(nameof(PickerPanelColumn));
        OnPropertyChanged(nameof(InspectorPanelColumn));
        OnPropertyChanged(nameof(PanelHorizontalAlignment));
        OnPropertyChanged(nameof(PanelZIndex));
        OnPropertyChanged(nameof(PickerPanelBorderThickness));
    }

    // ═══════════════════════════════════════════════
    // IPageLifecycle（M3：构造器不再 fire-and-forget 加载）
    // ═══════════════════════════════════════════════

    /// <summary>是否存在未保存更改（供 <see cref="IPageLifecycle"/> 与导航拦截使用）。</summary>
    public bool IsDirty => HasUnsavedChanges;

    public async Task OnEnterAsync(CancellationToken ct)
    {
        _enterCts?.Cancel();
        _enterCts?.Dispose();
        _enterCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // 切换页面自动收起抽屉
        IsPickerDrawerOpen = false;
        IsInspectorDrawerOpen = false;

        var token = _enterCts.Token;
        var completion = _enterCompletion;
        try
        {
            await RefreshDataAsync(token);
        }
        catch (OperationCanceledException)
        {
            // 离开页面取消，保持已有状态
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    public Task OnLeaveAsync()
    {
        _enterCts?.Cancel();
        _generateCts?.Cancel();
        IsPickerDrawerOpen = false;
        IsInspectorDrawerOpen = false;
        // 为下一次进入准备新的完成信号，保证引导读到的是「下一次加载」而非旧任务
        _enterCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════
    // IGuideSeedTarget（M5：引导演示注入/清理下沉到页面）
    // ═══════════════════════════════════════════════

    /// <summary>注入 3×4 演示座位与演示名单（纯内存，不落盘）。</summary>
    public void SeedGuideData()
    {
        // 已等待 OnEnterAsync 内的 RefreshDataAsync 完成，注入不会被后续加载覆盖
        VenueItems.Clear();
        VenueItems.Add(new("demo-v", "演示教室"));
        DatasetItems.Clear();
        DatasetItems.Add(new StudentDatasetInfo { Id = "demo-ds", Name = "演示班级", StudentCount = 6 });
        SelectedVenue = VenueItems.FirstOrDefault();
        SelectedDataset = DatasetItems.FirstOrDefault();

        var names = new[] { "Alice", "Bob", "Charlie", "Diana", "Eve", "Frank" };
        var seats = new ObservableCollection<SeatDisplayItem>();
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 3; c++)
            {
                var idx = (r * 3) + c;
                seats.Add(new SeatDisplayItem
                {
                    SeatId = $"R{r}C{c}",
                    SeatLabel = $"R{r}C{c}",
                    X = 200 + (c * 80),
                    Y = 200 + (r * 60),
                    Width = 50,
                    Height = 30,
                    IsOccupied = idx < 6,
                    StudentName = idx < 6 ? names[idx] : null,
                    OccupancyStatus = idx < 6 ? SeatOccupancyStatus.Occupied : SeatOccupancyStatus.Empty
                });
            }

        SeatItems = seats;
        OverlayItems = new ObservableCollection<SeatDisplayItem>();
        TotalSeats = 12;
        AssignedSeats = 6;
        HasGenerated = true;
        IsGenerating = false;
        StatusMessage = "已分配 6/12 个座位（演示数据）";
        // 让演示座位真正渲染到自绘画布（SeatItems 变化不会自动重建快照）
        UpdateCanvasSnapshot();
    }

    /// <summary>清空演示工作区（与旧 OnboardingService.ClearPageData 行为一致）。</summary>
    public void ClearGuideData()
    {
        HasGenerated = false;
        SeatItems.Clear();
        OverlayItems.Clear();
        TotalSeats = 0;
        AssignedSeats = 0;
        VenueItems.Clear();
        DatasetItems.Clear();
        SelectedVenue = null;
        SelectedDataset = null;
        // 列表已清空 → 下次进入重新加载（页面缓存策略失效化）
        InvalidateData();
    }

    // ── 抽屉命令 ──

    [RelayCommand]
    private void TogglePickerDrawer()
    {
        IsPickerDrawerOpen = !IsPickerDrawerOpen;
        if (IsPickerDrawerOpen) IsInspectorDrawerOpen = false;
    }

    [RelayCommand]
    private void ToggleInspectorDrawer()
    {
        IsInspectorDrawerOpen = !IsInspectorDrawerOpen;
        if (IsInspectorDrawerOpen) IsPickerDrawerOpen = false;
    }

    [RelayCommand]
    private void CloseDrawers()
    {
        IsPickerDrawerOpen = false;
        IsInspectorDrawerOpen = false;
    }

    // ═══════════════════════════════════════════════
    // IFileDropHandler：.seatsets 数据包导入（原 Home 页职责，M3 迁移到默认入口页）
    // ═══════════════════════════════════════════════

    IReadOnlyList<string> IFileDropHandler.AcceptedFileExtensions { get; } = [".seatsets"];

    async Task<bool> IFileDropHandler.HandleFileDropAsync(IReadOnlyList<string> filePaths, CancellationToken ct)
    {
        if (filePaths.Count == 0) return false;
        var ok = await SeatSetsImportHelper.ImportAsync(filePaths[0], _services, Dialog, _logger, ct);
        if (ok)
        {
            InvalidateData();
            await RefreshDataAsync(ct);
        }
        return ok;
    }

    /// <summary>用于抑制 <see cref="OnSelectedVenueChanged"/> 覆盖已恢复的布局。</summary>
    private bool _isRestoringWorkspace;

    // ── 初始化 ──

    private async Task LoadDefaultZoomAsync()
    {
        try
        {
            var settings = await _facade.LoadAppSettingsAsync();
            _defaultZoomLevel = settings.DefaultZoomLevel;
            ZoomLevel = _defaultZoomLevel;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载默认缩放失败，使用 1.0");
        }
    }

    /// <summary>会场/名单列表是否已加载（页面缓存策略：进入不重复加载，显式刷新）。</summary>
    private bool _dataLoaded;

    /// <summary>标记列表数据失效（下次进入或刷新时重新加载）。</summary>
    public void InvalidateData() => _dataLoaded = false;

    /// <summary>显式刷新会场/名单列表（页面缓存策略的手动入口）。</summary>
    [RelayCommand]
    private async Task ReloadDataAsync()
    {
        _dataLoaded = false;
        await RefreshDataAsync();
    }

    public async Task RefreshDataAsync(CancellationToken ct = default)
    {
        if (!_dataLoaded)
        {
            await Task.WhenAll(LoadVenuesAsync(), LoadDatasetsAsync(), LoadDefaultZoomAsync());
            ct.ThrowIfCancellationRequested();
            _dataLoaded = true;
            StatusMessage = Resources.Seating_ReadyHint;
        }
        else
        {
            // 重新进入页面时轻量刷新会场名称（会场可在「会场与布局」页被重命名/增删）
            await RefreshVenueSummariesAsync(ct);
        }
        await TryRestoreWorkspaceAsync();
    }

    [RelayCommand]
    private async Task LoadVenuesAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var summaries = await _facade.ListVenueSummariesAsync();
            VenueItems = new ObservableCollection<VenueItem>(
                summaries
                    .OrderBy(s => s.Name, NaturalStringComparer.Instance)
                    .ThenBy(s => s.Id, StringComparer.Ordinal)
                    .Select(s => new VenueItem(s.Id, s.Name)));
        });
    }

    /// <summary>
    /// 轻量刷新会场列表（摘要读取，不反序列化布局）：按 Id 保留选中项，
    /// 名称变化时原位替换，增删会场时同步集合。
    /// </summary>
    private async Task RefreshVenueSummariesAsync(CancellationToken ct)
    {
        var summaries = (await _facade.ListVenueSummariesAsync(ct))
            .OrderBy(s => s.Name, NaturalStringComparer.Instance)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
        ct.ThrowIfCancellationRequested();

        var selectedId = SelectedVenue?.Id;
        _isRestoringWorkspace = true;
        try
        {
            var byId = summaries.ToDictionary(s => s.Id);
            for (var i = VenueItems.Count - 1; i >= 0; i--)
            {
                if (!byId.ContainsKey(VenueItems[i].Id))
                    VenueItems.RemoveAt(i);
            }

            foreach (var summary in summaries)
            {
                var index = -1;
                for (var i = 0; i < VenueItems.Count; i++)
                {
                    if (VenueItems[i].Id == summary.Id) { index = i; break; }
                }

                if (index >= 0)
                {
                    if (VenueItems[index].Name != summary.Name)
                        VenueItems[index] = new VenueItem(summary.Id, summary.Name);
                }
                else
                {
                    VenueItems.Add(new VenueItem(summary.Id, summary.Name));
                }
            }

            // 名称自然排序：把现有项移动到有序位置（新增项已按 summaries 顺序追加）
            for (var target = 0; target < summaries.Count; target++)
            {
                var targetId = summaries[target].Id;
                var current = -1;
                for (var i = target; i < VenueItems.Count; i++)
                {
                    if (VenueItems[i].Id == targetId) { current = i; break; }
                }

                if (current > target)
                    VenueItems.Move(current, target);
            }

            if (!string.IsNullOrEmpty(selectedId))
                SelectedVenue = VenueItems.FirstOrDefault(v => v.Id == selectedId) ?? SelectedVenue;
        }
        finally
        {
            _isRestoringWorkspace = false;
        }
    }

    [RelayCommand]
    private async Task LoadDatasetsAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var datasets = await _facade.ListStudentDatasetsAsync();
            DatasetItems = new ObservableCollection<StudentDatasetInfo>(datasets);
        });
    }

    /// <summary>如果有活跃工作区（如快照回滚后），恢复座位图显示。</summary>
    private async Task TryRestoreWorkspaceAsync()
    {
        _workspace = await _facade.GetCurrentWorkspaceAsync();
        if (_workspace == null) return;

        _currentLayout = await _facade.GetCurrentLayoutAsync();
        _currentPlan = _workspace.BuildSeatingPlan();

        if (_currentLayout != null)
        {
            _isRestoringWorkspace = true;
            try
            {
                var restored = VenueItems.FirstOrDefault(v => v.Id == _currentLayout.Id)
                             ?? VenueItems.FirstOrDefault();
                // M6：首屏列表以 ID 占位，恢复工作区时回填真实名称
                if (restored is not null && restored.Name == restored.Id && !string.IsNullOrEmpty(_currentLayout.Name))
                {
                    var index = VenueItems.IndexOf(restored);
                    if (index >= 0)
                    {
                        restored = restored with { Name = _currentLayout.Name };
                        VenueItems[index] = restored;
                    }
                }
                SelectedVenue = restored;
            }
            finally
            {
                _isRestoringWorkspace = false;
            }

            HasGenerated = true;
        }

        await UpdateRightPanelAsync();
        UpdateStats();
        InitHistory(Resources.Seating_RestoredWorkspace);
        StatusMessage = string.Format(Resources.Seating_RestoredWorkspaceFmt, AssignedSeats, TotalSeats);

        // 强制在 UI 线程上重绘，确保异步 continuation 未切到线程池时也能正确渲染
        Dispatcher.UIThread.Post(RefreshPreview);
    }

    // ── 会场选择 ──
    partial void OnSelectedVenueChanged(VenueItem? value)
    {
        OnPropertyChanged(nameof(WorkbenchSubtitle));
        if (value == null || _isRestoringWorkspace) return;
        var venueId = value.Id;
        _ = SafeExecuteAsync(async () =>
        {
            var layout = await _facade.LoadVenueAsync(venueId);
            // 竞态防护：加载期间用户可能已切换到其他会场，后完成者不得覆盖当前布局
            if (SelectedVenue?.Id != venueId) return;

            _currentLayout = layout;
            if (_currentLayout != null)
            {
                ObstacleProcessor.ApplyObstacles(_currentLayout);
                ApplyVenueDisplayName(value, _currentLayout.Name);
                StatusMessage = string.Format(Resources.Seating_VenueLoadedFmt, _currentLayout.Name, _currentLayout.Seats.Count);
            }
        });
    }

    /// <summary>
    /// M6 启动优化：会场列表首屏以 ID 占位显示，布局加载完成后回填真实名称。
    /// 集合项替换可能触发 ListBox 选择变化（清空或重映射）；先进入恢复门再替换，
    /// 之后按 Id 恢复选中，保证不发生二次加载（<see cref="_isRestoringWorkspace"/>）。
    /// </summary>
    private void ApplyVenueDisplayName(VenueItem item, string? name)
    {
        if (string.IsNullOrEmpty(name) || item.Name != item.Id || name == item.Id) return;

        var index = VenueItems.IndexOf(item);
        if (index < 0) return;

        var updated = item with { Name = name };
        _isRestoringWorkspace = true;
        try
        {
            VenueItems[index] = updated;
            // 只要用户没有切走就恢复选中（替换可能已把它清空/保留旧实例）
            if (SelectedVenue is null || SelectedVenue.Id == updated.Id)
                SelectedVenue = updated;
        }
        finally
        {
            _isRestoringWorkspace = false;
        }
    }

    partial void OnSelectedDatasetChanged(StudentDatasetInfo? value)
        => OnPropertyChanged(nameof(WorkbenchSubtitle));

    // ── 生成座位 ──

    [RelayCommand]
    private async Task GenerateSeatingAsync()
    {
        if (!CanGenerate || _currentLayout == null) return;

        _generateCts?.Cancel();
        _generateCts = new CancellationTokenSource();
        var ct = _generateCts.Token;

        IsGenerating = true;
        HasGenerated = false;
        StatusMessage = Resources.Seating_Generating;

        await SafeCancelableAsync(async () =>
        {
            // 1. 加载学生
            var students = await _facade.LoadStudentDatasetAsync(SelectedDataset!.Id, ct);
            if (students == null || students.Count == 0)
            {
                StatusMessage = Resources.Seating_NoMembers;
                return;
            }

            // 2. 写入临时 JSON 文件（RosterFile 格式）
            var roster = new RosterFile { Version = "1.0", Students = students };
            var jsonOptions = JsonOptions.WriteIndentedCamelCase;
            var tempPath = Path.Combine(Path.GetTempPath(), $"a_pair_gen_{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(roster, jsonOptions), ct);

            try
            {
                // 3. 调用生成
                var progress = new Progress<SeatingProgress>(p =>
                {
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        StatusMessage = p.StatusMessage;
                    });
                });

                var request = new SeatingRequest
                {
                    LayoutId = SelectedVenue!.Id,
                    DatasetId = SelectedDataset!.Id,
                    StudentDataSource = tempPath,
                    Description = string.Format(Resources.Seating_VenueDatasetDesc, SelectedVenue.Name, SelectedDataset.Name)
                };

                _workspace = await _facade.GenerateSeatingAsync(request, progress, ct);
                _currentPlan = _workspace.BuildSeatingPlan();

                // 4. 构建显示 + 初始化历史
                BuildSeatDisplayItems();
                await UpdateRightPanelAsync();
                UpdateStats();
                InitHistory(Resources.Seating_GenerateDesc);

                HasGenerated = true;
                _counterService.Increment();
                StatusMessage = string.Format(Resources.Seating_GeneratedFmt, AssignedSeats, TotalSeats);
            }
            finally
            {
                // 5. 清理临时文件
                try { File.Delete(tempPath); } catch { /* 忽略 */ }
            }
        }, Resources.Seating_GenerateFailed);

        IsGenerating = false;
    }

    /// <summary>
    /// 创建空白会场：加载座位布局和学生但不执行策略管道。
    /// 所有学生进入未分配列表，座位全部为空。
    /// </summary>
    [RelayCommand]
    private async Task CreateEmptySeatingAsync()
    {
        if (!CanCreateEmpty || _currentLayout == null) return;

        _generateCts?.Cancel();
        _generateCts = new CancellationTokenSource();
        var ct = _generateCts.Token;

        IsGenerating = true;
        HasGenerated = false;
        StatusMessage = Resources.Seating_CreateEmptyHint;

        await SafeCancelableAsync(async () =>
        {
            // 加载学生
            var students = await _facade.LoadStudentDatasetAsync(SelectedDataset!.Id, ct);
            if (students == null || students.Count == 0)
            {
                StatusMessage = Resources.Seating_NoMembers;
                return;
            }

            // 通过 facade 创建空白工作区（加载布局 + 学生，不执行策略）
            _workspace = await _facade.CreateEmptyWorkspaceAsync(
                SelectedVenue!.Id, SelectedDataset!.Id, ct);
            _currentPlan = _workspace.BuildSeatingPlan();

            // 构建显示 + 初始化历史
            BuildSeatDisplayItems();
            await UpdateRightPanelAsync();
            UpdateStats();
            InitHistory(Resources.Seating_EmptyStartDesc);

            HasGenerated = true;
            StatusMessage = string.Format(Resources.Seating_EmptyCreatedFmt, TotalSeats, UnassignedStudentCount);
        }, Resources.Seating_CreateEmptyFailed);

        IsGenerating = false;
    }

    // ── Canvas 数据构建 ──

    private void BuildSeatDisplayItems()
    {
        if (_currentLayout == null || _workspace == null || _currentPlan == null) return;

        var metadata = _currentLayout.Metadata;
        var studentMap = _workspace.Students.ToDictionary(s => s.Id, s => s.Name);
        var assignments = _currentPlan.Assignments;

        // Grid 与会场预览共用同一视觉几何（0.8 同桌压缩 + 可读尺寸坐标放大），保证两处一致
        var gridGeometry = metadata is GridLayoutMetadata gm ? GridVisualGeometryBuilder.Build(gm) : null;
        var gridSeatsByPosition = gridGeometry?.Seats
            .Where(s => !s.IsDisabled)
            .ToDictionary(s => (s.Row, s.Column));
        var (baseW, baseH) = gridGeometry is { } g
            ? (g.SeatWidth, g.SeatHeight)
            : GetSeatDimensions(metadata);
        double factorX = gridGeometry?.FactorX ?? 1.0;
        double factorY = gridGeometry?.FactorY ?? 1.0;

        // 第一遍：收集原始坐标范围
        double minX0 = double.MaxValue, minY0 = double.MaxValue;
        double maxX0 = 0, maxY0 = 0;
        var rawPositions = new List<(double cx, double cy, Seat seat)>();
        foreach (var seat in _currentLayout.Seats)
        {
            if (!seat.IsAvailable) continue;
            double cx, cy;
            if (gridSeatsByPosition is not null
                && seat is GridSeat gridSeat
                && gridSeatsByPosition.TryGetValue((gridSeat.Row, gridSeat.Column), out var visual))
            {
                (cx, cy) = (visual.X, visual.Y);
            }
            else
            {
                (cx, cy) = SeatGeometryHelper.GetPosition(seat, metadata);
                cx *= factorX;
                cy *= factorY;
                if (seat is PolarSeat) { cx -= baseW / 2; cy -= baseH / 2; }
            }
            rawPositions.Add((cx, cy, seat));
            minX0 = Math.Min(minX0, cx);
            minY0 = Math.Min(minY0, cy);
            maxX0 = Math.Max(maxX0, cx + baseW);
            maxY0 = Math.Max(maxY0, cy + baseH);
        }
        // 将障碍物也纳入包围盒
        foreach (var obs in _currentLayout.Obstacles)
        {
            double ow = (obs.Width > 0 ? obs.Width : 60) * factorX;
            double oh = (obs.Height > 0 ? obs.Height : 40) * factorY;
            double ox = obs.X * factorX;
            double oy = obs.Y * factorY;
            minX0 = Math.Min(minX0, ox);
            minY0 = Math.Min(minY0, oy);
            maxX0 = Math.Max(maxX0, ox + ow);
            maxY0 = Math.Max(maxY0, oy + oh);
        }

        // 空布局保护（无可用座位时清空画面）
        if (rawPositions.Count == 0)
        {
            SeatItems = [];
            OverlayItems = [];
            CanvasSnapshot = null;
            return;
        }

        // 第二遍：坐标归一化到板面左上（含内边距）；缩放/平移由 SeatingCanvas 承担
        const double localMargin = 40;
        double seatWidth = baseW;
        double seatHeight = baseH;
        double spanX = maxX0 - minX0;
        double spanY = maxY0 - minY0;
        CanvasWidth = Math.Max(160, spanX + (localMargin * 2));
        CanvasHeight = Math.Max(160, spanY + (localMargin * 2));

        var items = new List<SeatDisplayItem>();
        int seatCounter = 0;

        foreach (var (cx, cy, seat) in rawPositions)
        {
            var occupantId = assignments.GetValueOrDefault(seat.Id);
            bool isOccupied = occupantId != null;
            bool isFrontRow = IsFrontRowSeat(seat, metadata);

            double sx = cx - minX0 + localMargin;
            double sy = cy - minY0 + localMargin;

            seatCounter++;
            items.Add(new SeatDisplayItem
            {
                X = sx,
                Y = sy,
                Width = seatWidth,
                Height = seatHeight,
                SeatId = seat.Id,
                SeatLabel = BuildSeatLabel(seat, seatCounter),
                IsFrontRow = isFrontRow,
                StudentName = isOccupied ? studentMap.GetValueOrDefault(occupantId!, "") : null,
                StudentId = occupantId,
                IsOccupied = isOccupied,
                IsFixed = seat.IsFixed,
                IsSelectedForSwap = _swapSourceSeat?.SeatId == seat.Id,
                OccupancyStatus = isOccupied
                    ? (seat.IsFixed ? SeatOccupancyStatus.Fixed : SeatOccupancyStatus.Occupied)
                    : SeatOccupancyStatus.Empty
            });
        }
        SeatItems = new ObservableCollection<SeatDisplayItem>(items);

        // 障碍物叠加层（坐标同样归一化；Grid 讲台水平居中于座位范围）
        var overlays = new List<SeatDisplayItem>();
        var boardOverlays = new List<BoardOverlay>();
        double podiumW = baseW * 2.5;
        double podiumH = baseH * 1.6;
        double doorW = baseW * 1.2;
        double doorH = baseH * 0.9;

        double seatMinX = double.MaxValue, seatMaxX = 0;
        foreach (var seat in _currentLayout.Seats)
        {
            if (!seat.IsAvailable) continue;
            double cx;
            if (gridSeatsByPosition is not null
                && seat is GridSeat gridSeat
                && gridSeatsByPosition.TryGetValue((gridSeat.Row, gridSeat.Column), out var visual))
                cx = visual.X;
            else
                cx = SeatGeometryHelper.GetPosition(seat, metadata).X;
            seatMinX = Math.Min(seatMinX, cx);
            seatMaxX = Math.Max(seatMaxX, cx + baseW);
        }

        foreach (var obs in _currentLayout.Obstacles)
        {
            bool isDoor = string.Equals(obs.Type, "Door", StringComparison.OrdinalIgnoreCase);
            double w = obs.Width > 0 ? obs.Width : (obs.Type == "Podium" ? podiumW : doorW);
            double h = obs.Height > 0 ? obs.Height : (obs.Type == "Podium" ? podiumH : doorH);
            // 与会场预览一致：门为固定尺寸，其余按该布局的视觉放大系数缩放
            if (gridGeometry is null || !isDoor)
            {
                w *= factorX;
                h *= factorY;
            }

            double obsX = obs.X * factorX;
            double obsY = obs.Y * factorY;
            // Grid 讲台强制居中（seatMin/Max 已按同一系数放大）
            if (metadata is GridLayoutMetadata && obs.Type == "Podium" && seatMinX < seatMaxX)
                obsX = ((seatMinX + seatMaxX) / 2) - (w / 2);

            double ox = obsX - minX0 + localMargin;
            double oy = obsY - minY0 + localMargin;

            overlays.Add(new SeatDisplayItem
            {
                X = ox,
                Y = oy,
                Width = w,
                Height = h,
                SeatId = obs.Id,
                SeatLabel = obs.Type ?? Resources.Seating_Obstacle,
                CornerRadius = obs.Type == "Podium" ? new(w / 2) : new(4),
                OccupancyStatus = SeatOccupancyStatus.Empty
            });
            boardOverlays.Add(new BoardOverlay(
                ox, oy, w, h,
                obs.Type ?? Resources.Seating_Obstacle,
                IsRound: obs.Type == "Podium",
                IsDoor: string.Equals(obs.Type, "Door", StringComparison.OrdinalIgnoreCase)));
        }
        OverlayItems = new ObservableCollection<SeatDisplayItem>(overlays);

        CanvasSnapshot = new SeatLayoutSnapshot(
            items.Select(ToSeatVisual).ToList(),
            CanvasWidth, CanvasHeight, ++_snapshotVersion, boardOverlays);
    }

    /// <summary>由显示项生成不可变座位视觉（缩放/平移由画布矩阵处理）。</summary>
    private SeatVisual ToSeatVisual(SeatDisplayItem item) => new(
        item.SeatId, item.X, item.Y, item.Width, item.Height,
        IsOccupied: item.IsOccupied,
        IsFixed: item.IsFixed,
        IsDisabled: false,
        Label: item.IsOccupied ? item.StudentName : null,
        SeatLabel: item.SeatLabel,
        StudentId: item.StudentId,
        IsSwapSource: item.IsSelectedForSwap || (_swapSourceSeat?.SeatId == item.SeatId),
        IsDropTarget: item.IsDragHover,
        IsDataStale: item.IsDataStale);

    /// <summary>轻量刷新画布快照（不重算几何；用于交换选中态等 UI 状态变化）。</summary>
    public void UpdateCanvasSnapshot()
    {
        var overlays = OverlayItems.Select(o => new BoardOverlay(
            o.X, o.Y, o.Width, o.Height, o.SeatLabel,
            IsRound: Math.Abs(o.CornerRadius.TopLeft - (o.Height / 2)) < 0.01,
            IsDoor: string.Equals(o.SeatLabel, "Door", StringComparison.OrdinalIgnoreCase))).ToList();

        CanvasSnapshot = new SeatLayoutSnapshot(
            SeatItems.Select(ToSeatVisual).ToList(),
            CanvasWidth, CanvasHeight, ++_snapshotVersion, overlays);
    }

    private static (double width, double height) GetSeatDimensions(LayoutMetadata metadata)
        => ComputeSeatSize(metadata);

    /// <summary>按间距计算座位的安全尺寸（宽<最近邻间距的70%）。</summary>
    private static (double w, double h) ComputeSeatSize(LayoutMetadata metadata)
    {
        if (metadata is GridLayoutMetadata gm)
        {
            double intra = gm.IntraDeskSpacing > 0 ? gm.IntraDeskSpacing : 20;
            double inter = gm.InterDeskSpacing > 0 ? gm.InterDeskSpacing : 64;
            double colGap = gm.SeatsPerDesk > 1 ? Math.Min(intra, inter) : inter;
            double rowGap = gm.VerticalSpacing > 0 ? gm.VerticalSpacing : 56;
            double w = Math.Clamp(colGap * 0.95, 44, 72);
            double h = Math.Clamp(rowGap * 0.62, 24, 44);
            return (w, h);
        }
        if (metadata is PolarLayoutMetadata pm)
        {
            double step = pm.RadiusStep > 0 ? pm.RadiusStep : 40;
            double s = Math.Clamp(step * 0.85, 28, 48);
            return (s, s);
        }
        return (42, 26);
    }

    // ── 座位标签与行列判断 ──

    private static string BuildSeatLabel(Seat seat, int counter)
    {
        return seat switch
        {
            GridSeat g => $"R{g.Row}C{g.Column}",
            PolarSeat p => string.Format(Resources.Seating_PolarLabelFmt, p.Ring, p.AngleDegrees),
            FreeformSeat => $"#{counter}",
            _ => $"#{counter}"
        };
    }

    private static bool IsFrontRowSeat(Seat seat, LayoutMetadata metadata)
    {
        return (seat, metadata) switch
        {
            (GridSeat g, GridLayoutMetadata gm) => g.Row <= gm.FrontRowCount,
            (PolarSeat p, PolarLayoutMetadata pm) => IsPolarFrontRow(p, pm),
            _ => false
        };
    }

    private static bool IsPolarFrontRow(PolarSeat seat, PolarLayoutMetadata meta)
    {
        int totalRings = meta.RingSeatCounts.Count > 0 ? meta.RingSeatCounts.Count : meta.Rings;
        int frontCount = Math.Min(meta.FrontRowCount, totalRings);
        return seat.Ring > (totalRings - frontCount);
    }

    // ── 显示刷新 ──

    private void RefreshSeatAssignments()
    {
        if (_currentPlan == null || _workspace == null) return;
        var studentMap = _workspace.Students.ToDictionary(s => s.Id, s => s.Name);
        var assignments = _currentPlan.Assignments;

        foreach (var item in SeatItems)
        {
            var occupantId = assignments.GetValueOrDefault(item.SeatId);
            bool isOccupied = occupantId != null;
            item.StudentName = isOccupied ? studentMap.GetValueOrDefault(occupantId!, "") : null;
            item.StudentId = occupantId;
            item.IsOccupied = isOccupied;
            item.OccupancyStatus = isOccupied
                ? (item.IsFixed ? SeatOccupancyStatus.Fixed : SeatOccupancyStatus.Occupied)
                : SeatOccupancyStatus.Empty;
            item.IsSelectedForSwap = false;
        }

        UpdateCanvasSnapshot();
    }

    private async Task UpdateRightPanelAsync()
    {
        // 策略列表
        var allStrategies = await _facade.GetStrategiesAsync();

        // 分离独立策略和依赖策略
        var enabledVisible = allStrategies
            .Where(s => s.IsEnabled && s.Visible)
            .OrderByDescending(s => s.Priority)
            .ToList();
        var independents = enabledVisible.Where(s => s.IsIndependent).ToList();
        var dependents = enabledVisible.Where(s => !s.IsIndependent).ToList();

        // 将依赖策略注入到 RandomFill 的 DependentChildren
        var randomFill = independents.FirstOrDefault(s => s.Id == RandomFillStrategy.StrategyId);
        if (randomFill != null && dependents.Count > 0)
        {
            randomFill.DependentChildren = [.. dependents.OrderByDescending(d => d.Priority)];
        }

        ActiveStrategies = new ObservableCollection<StrategyDisplayInfo>(independents);

        // 未分配学生
        if (_workspace != null && _currentPlan != null)
        {
            var assignedIds = new HashSet<string>(_currentPlan.Assignments.Values.Where(v => v != null)!);
            UnassignedStudents = new ObservableCollection<Student>(
                Helpers.StudentSorter.Sort(_workspace.Students.Where(s => !assignedIds.Contains(s.Id))));
            OnPropertyChanged(nameof(UnassignedStudentCount));
        }

        // 策略消息（按 Warning / Error 分组）
        if (_workspace != null)
        {
            // 构建消息模板字典：从各策略的 manifest messages 中解析当前语言模板
            var templates = new Dictionary<string, string>();
            foreach (var s in allStrategies)
            {
                if (s.Messages is null) continue;
                foreach (var (key, dict) in s.Messages)
                    templates[key] = Helpers.LocalizeHelper.Resolve(dict);
            }

            var messages = _workspace.Messages;
            var studentNames = _workspace.Students.ToDictionary(s => s.Id, s => s.Name);
            var errors = messages.Where(m => m.Severity == StrategyMessageSeverity.Error).ToList();
            var warnings = messages.Where(m => m.Severity == StrategyMessageSeverity.Warning).ToList();

            var groups = new ObservableCollection<StrategyMessageGroup>();
            if (errors.Count > 0)
                groups.Add(new StrategyMessageGroup
                {
                    Severity = StrategyMessageSeverity.Error,
                    Title = $"{Resources.Seating_MessagesGroupError} ({errors.Count})",
                    Messages = new ObservableCollection<StrategyMessageItem>(
                        errors.Select(m => new StrategyMessageItem
                        {
                            StrategyName = m.StrategyDisplayName,
                            Message = FormatStrategyMessage(m, studentNames, templates),
                            Severity = m.Severity
                        }))
                });
            if (warnings.Count > 0)
                groups.Add(new StrategyMessageGroup
                {
                    Severity = StrategyMessageSeverity.Warning,
                    Title = $"{Resources.Seating_MessagesGroupWarning} ({warnings.Count})",
                    Messages = new ObservableCollection<StrategyMessageItem>(
                        warnings.Select(m => new StrategyMessageItem
                        {
                            StrategyName = m.StrategyDisplayName,
                            Message = FormatStrategyMessage(m, studentNames, templates),
                            Severity = m.Severity
                        }))
                });

            MessageGroups = groups;
        }
        else
        {
            MessageGroups = [];
        }
        OnPropertyChanged(nameof(HasMessages));
    }

    private void UpdateStats()
    {
        TotalSeats = SeatItems.Count;
        AssignedSeats = SeatItems.Count(s => s.IsOccupied);
    }

    /// <summary>手动操作后统一刷新显示、更新历史、设置状态消息。</summary>
    private async Task FinalizeManualOperationAsync(string historyDesc, string statusMsg)
    {
        _currentPlan = _workspace!.BuildSeatingPlan();
        RefreshSeatAssignments();
        await UpdateRightPanelAsync();
        UpdateStats();
        AddHistoryEntry(historyDesc);
        StatusMessage = statusMsg;
    }

    // ── 座位点击交换 ──

    [RelayCommand]
    private async Task ClickSeatAsync(SeatDisplayItem? clickedSeat)
    {
        if (clickedSeat == null || _workspace == null) return;

        // ── 从右侧未分配列表点击放置 ──
        if (SelectedUnassignedStudent != null && !clickedSeat.IsOccupied && !clickedSeat.IsFixed)
        {
            var student = SelectedUnassignedStudent;
            var studentName = student.Name;
            var seatLabel = clickedSeat.SeatLabel;

            await SafeExecuteAsync(async () =>
            {
                var assignCmd = new AssignSeatCommand(clickedSeat.SeatId, student.Id);
                var ok = await _facade.ExecuteCommandAsync(assignCmd, recordInHistory: false);
                if (ok)
                {
                    await FinalizeManualOperationAsync(
                        string.Format(Resources.Seating_PlacedFmt, studentName, seatLabel),
                        string.Format(Resources.Seating_PlacedFmt, studentName, seatLabel));
                    SelectedUnassignedStudent = null;
                }
            }, Resources.Seating_PlaceTitle);
            return;
        }

        // 首次点击：选择源座位
        if (_swapSourceSeat == null)
        {
            if (!clickedSeat.IsOccupied && !clickedSeat.IsFixed) return;

            _swapSourceSeat = clickedSeat;
            clickedSeat.IsSelectedForSwap = true;
            UpdateCanvasSnapshot();
            IsSwapMode = true;
            SwapHintText = string.Format(Resources.Seating_SelectTargetFmt, clickedSeat.StudentName ?? clickedSeat.SeatLabel);
            return;
        }

        var source = _swapSourceSeat;

        // 点击同一座位 = 取消选择
        if (source.SeatId == clickedSeat.SeatId)
        {
            CancelSwap();
            return;
        }

        // 执行交换
        await SafeExecuteAsync(async () =>
        {
            var swapCmd = new SwapSeatCommand(
                (source.SeatId, source.StudentId),
                (clickedSeat.SeatId, clickedSeat.IsOccupied ? clickedSeat.StudentId : null));

            var ok = await _facade.ExecuteCommandAsync(swapCmd, recordInHistory: false);
            if (ok)
            {
                await FinalizeManualOperationAsync(
                    string.Format(Resources.Seating_SwapDescFmt, source.StudentName ?? source.SeatLabel, clickedSeat.StudentName ?? Resources.Common_Cancel),
                    string.Format(Resources.Seating_SwappedFmt, source.StudentName, clickedSeat.StudentName ?? Resources.Common_Cancel));
                CancelSwap();
            }
        }, Resources.Seating_SwapFailed);
    }

    [RelayCommand]
    private void CancelSwap()
    {
        _swapSourceSeat?.IsSelectedForSwap = false;
        UpdateCanvasSnapshot();
        _swapSourceSeat = null;
        IsSwapMode = false;
        SwapHintText = string.Empty;
    }

    /// <summary>
    /// 将交换模式下选中的源座位学生移除到未分配列表。
    /// </summary>
    [RelayCommand]
    private async Task RemoveToTrashAsync()
    {
        if (_workspace == null || _swapSourceSeat == null) return;
        if (!_swapSourceSeat.IsOccupied || _swapSourceSeat.IsFixed) return;

        var studentName = _swapSourceSeat.StudentName ?? "";
        var seatLabel = _swapSourceSeat.SeatLabel;

        await SafeExecuteAsync(async () =>
        {
            var removeCmd = new RemoveStudentCommand(_swapSourceSeat.SeatId);
            var ok = await _facade.ExecuteCommandAsync(removeCmd, recordInHistory: false);
            if (ok)
            {
                await FinalizeManualOperationAsync(
                    string.Format(Resources.Seating_RemovedFmt, studentName, seatLabel),
                    string.Format(Resources.Seating_RemovedFmt, studentName, seatLabel));
                CancelSwap();
            }
        }, Resources.Seating_RemoveTitle);
    }

    // ── 拖放操作（由 code-behind 调用） ──

    /// <summary>
    /// 获取学生的显示名称（从工作区中查找）。
    /// </summary>
    internal string GetStudentName(string studentId)
        => _workspace?.Students.FirstOrDefault(s => s.Id == studentId)?.Name ?? studentId;

    /// <summary>
    /// 执行拖放放置操作，支持四种情况：
    /// 1. 从未分配列表 → 空座位：AssignSeatCommand
    /// 2. 从座位 → 空座位：SwapSeatCommand（移动）
    /// 3. 从座位 → 已占座位：SwapSeatCommand（交换）
    /// 4. 从未分配列表 → 已占座位：不允许
    /// </summary>
    internal async Task<bool> ExecuteDropAsync(
        string studentId, string? sourceSeatId, string targetSeatId,
        CancellationToken ct = default)
    {
        if (_workspace == null) return false;

        // 拖到自身 = 无操作
        if (sourceSeatId == targetSeatId) return false;

        var targetItem = SeatItems.FirstOrDefault(s => s.SeatId == targetSeatId);
        if (targetItem == null || targetItem.IsFixed) return false;

        var studentName = GetStudentName(studentId);
        IUndoableCommand cmd;

        if (sourceSeatId == null && !targetItem.IsOccupied)
        {
            // 从未分配列表 → 空座位
            cmd = new AssignSeatCommand(targetSeatId, studentId);
        }
        else if (sourceSeatId != null && !targetItem.IsOccupied)
        {
            // 从座位 → 空座位（移动）
            cmd = new SwapSeatCommand(
                (sourceSeatId, studentId),
                (targetSeatId, null));
        }
        else if (sourceSeatId != null && targetItem.IsOccupied)
        {
            // 从座位 → 已占座位（交换）
            cmd = new SwapSeatCommand(
                (sourceSeatId, studentId),
                (targetSeatId, targetItem.StudentId));
        }
        else
        {
            // 从未分配列表 → 已占座位：不允许
            return false;
        }

        var ok = await _facade.ExecuteCommandAsync(cmd, ct, recordInHistory: false);
        if (ok)
        {
            await FinalizeManualOperationAsync(
                string.Format(Resources.Seating_PlacedFmt, studentName, targetItem.SeatLabel),
                string.Format(Resources.Seating_PlacedFmt, studentName, targetItem.SeatLabel));
            SelectedUnassignedStudent = null;
        }
        return ok;
    }

    /// <summary>
    /// 执行拖放到垃圾桶操作：将座位上的学生移除到未分配列表。
    /// </summary>
    internal async Task<bool> ExecuteRemoveToTrashAsync(string seatId, CancellationToken ct = default)
    {
        if (_workspace == null) return false;

        var item = SeatItems.FirstOrDefault(s => s.SeatId == seatId);
        if (item == null || !item.IsOccupied || item.IsFixed) return false;

        var studentName = item.StudentName ?? "";
        var seatLabel = item.SeatLabel;

        var cmd = new RemoveStudentCommand(seatId);
        var ok = await _facade.ExecuteCommandAsync(cmd, ct, recordInHistory: false);
        if (ok)
        {
            await FinalizeManualOperationAsync(
                string.Format(Resources.Seating_RemovedFmt, studentName, seatLabel),
                string.Format(Resources.Seating_RemovedFmt, studentName, seatLabel));
        }
        return ok;
    }

    // ── 撤销/重做（基于历史列表） ──

    [RelayCommand]
    private void Undo()
    {
        if (_workspace == null || _currentHistoryIndex <= 0) return;
        RestoreToHistoryIndex(_currentHistoryIndex - 1);
    }

    [RelayCommand]
    private void Redo()
    {
        if (_workspace == null || _currentHistoryIndex >= _historyEntries.Count - 1) return;
        RestoreToHistoryIndex(_currentHistoryIndex + 1);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedHistory))]
    public partial HistoryEntry? SelectedHistory { get; set; }

    public bool HasSelectedHistory => SelectedHistory != null;

    [RelayCommand]
    private void RestoreToSelected()
    {
        if (SelectedHistory == null || _workspace == null) return;
        var idx = _historyEntries.IndexOf(SelectedHistory);
        if (idx < 0) return;
        RestoreToHistoryIndex(idx);
        StatusMessage = string.Format(Resources.Seating_RestoredToFmt, SelectedHistory.Description);
    }

    // ── 历史管理 ──

    private void InitHistory(string description)
    {
        _historyEntries.Clear();
        var snapshot = CaptureSnapshot();
        _historyEntries.Add(new HistoryEntry(description, snapshot));
        _currentHistoryIndex = 0;
        _lastSavedIndex = -1; // 快照需用户手动保存，初始状态标记为"未保存"
        UpdateHistoryState();
    }

    private void AddHistoryEntry(string description)
    {
        // 删除当前位置之后的所有条目（新分支）
        while (_historyEntries.Count > _currentHistoryIndex + 1)
            _historyEntries.RemoveAt(_historyEntries.Count - 1);

        var snapshot = CaptureSnapshot();
        _historyEntries.Add(new HistoryEntry(description, snapshot));
        _currentHistoryIndex = _historyEntries.Count - 1;
        UpdateHistoryState();
    }

    private Dictionary<string, string> CaptureSnapshot()
        => _currentPlan != null ? new Dictionary<string, string>(_currentPlan.Assignments) : [];

    private void RestoreToHistoryIndex(int index)
    {
        if (_workspace == null || index < 0 || index >= _historyEntries.Count) return;

        _workspace.ApplySnapshotAssignments(_historyEntries[index].Assignments);
        _currentPlan = _workspace.BuildSeatingPlan();
        _currentHistoryIndex = index;
        UpdateHistoryState();

        _ = UpdateRightPanelAsync();
        RefreshSeatAssignments();
        UpdateStats();
        StatusMessage = string.Format(Resources.Seating_RestoredToFmt, _historyEntries[index].Description);
    }

    private void UpdateHistoryState()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(IsDirty));
        // 更新各条目的 IsCurrent 标记
        for (int i = 0; i < _historyEntries.Count; i++)
            _historyEntries[i].IsCurrent = i == _currentHistoryIndex;
    }

    // ── 保存到快照 ──

    [RelayCommand]
    private async Task SaveToSnapshotAsync()
    {
        if (!HasUnsavedChanges) return;

        await SafeExecuteAsync(async () =>
        {
            var snapshot = await _facade.CreateSnapshotAsync(string.Format(Resources.Seating_ManualSnapshotFmt, DateTime.Now.ToString("yyyy-MM-dd HH:mm")));
            if (snapshot != null)
            {
                _lastSavedIndex = _currentHistoryIndex;
                OnPropertyChanged(nameof(HasUnsavedChanges));
                StatusMessage = Resources.Seating_SnapshotSaved;
            }
        }, Resources.Seating_SnapshotFailed);
    }

    // ── 页面离开拦截 ──

    public override async Task<bool> CanLeaveAsync()
    {
        if (!HasUnsavedChanges)
        {
            _facade.ClearWorkspace();
            _ = _counterService.ReportAndResetAsync();
            return true;
        }

        var result = await Dialog.ShowMultiOptionAsync(Resources.Seating_UnsavedChanges,
            Resources.Seating_UnsavedChangesMsg,
            Resources.Seating_SaveAndLeave, Resources.Seating_DiscardAndLeave, "取消");

        switch (result)
        {
            case 0: // 保存
                await SaveToSnapshotAsync();
                break;
            case 1: // 不保存
                break;
            default: // 取消
                return false;
        }

        _facade.ClearWorkspace();
        _ = _counterService.ReportAndResetAsync();
        return true;
    }

    // ── 导出 ──

    private int _dialogLock;
    private static readonly TimeSpan ExportTimeout = TimeSpan.FromSeconds(30);

    [RelayCommand]
    private async Task ExportExcelAsync() => await ExportAsync(ExportFormat.Excel,
        [new FilePickerFileType(Resources.Data_ExcelFile) { Patterns = ["*.xlsx"] }], Resources.Seating_ExcelDefault);

    [RelayCommand]
    private async Task ExportCsvAsync() => await ExportAsync(ExportFormat.Csv,
        [new FilePickerFileType(Resources.Data_CSVFile) { Patterns = ["*.csv"] }], Resources.Seating_CsvDefault);

    [RelayCommand]
    private async Task ExportPdfAsync() => await ExportAsync(ExportFormat.Pdf,
        [new FilePickerFileType(Resources.Seating_PDFFile) { Patterns = ["*.pdf"] }], Resources.Seating_PDFDefault);

    [RelayCommand]
    private async Task ExportImageAsync() => await ExportAsync(ExportFormat.Png,
        [new FilePickerFileType(Resources.Seating_PNGFile) { Patterns = ["*.png"] }], Resources.Seating_PNGDefault);

    // ── 教师视角导出 ──

    [RelayCommand]
    private async Task ExportTeacherExcelAsync() => await ExportAsync(ExportFormat.Excel,
        [new FilePickerFileType(Resources.Data_ExcelFile) { Patterns = ["*.xlsx"] }], Resources.Seating_ExcelDefault, LayoutPerspective.TeacherView);

    [RelayCommand]
    private async Task ExportTeacherCsvAsync() => await ExportAsync(ExportFormat.Csv,
        [new FilePickerFileType(Resources.Data_CSVFile) { Patterns = ["*.csv"] }], Resources.Seating_CsvDefault, LayoutPerspective.TeacherView);

    [RelayCommand]
    private async Task ExportTeacherPdfAsync() => await ExportAsync(ExportFormat.Pdf,
        [new FilePickerFileType(Resources.Seating_PDFFile) { Patterns = ["*.pdf"] }], Resources.Seating_PDFDefault, LayoutPerspective.TeacherView);

    [RelayCommand]
    private async Task ExportTeacherImageAsync() => await ExportAsync(ExportFormat.Png,
        [new FilePickerFileType(Resources.Seating_PNGFile) { Patterns = ["*.png"] }], Resources.Seating_PNGDefault, LayoutPerspective.TeacherView);

    private ExportOptions BuildExportOptions(ExportFormat format, LayoutPerspective perspective)
    {
        var now = DateTime.Now;
        var viewLabel = perspective == LayoutPerspective.TeacherView
            ? Resources.Seating_TeacherView
            : Resources.Seating_StudentView;

        var infoParts = new List<string>();
        var labelSeparator = Resources.Seating_ExportLabelSeparator;
        var venueName = SelectedVenue?.Name ?? _currentLayout?.Name;
        if (!string.IsNullOrWhiteSpace(venueName))
            infoParts.Add($"{Resources.Seating_Venue}{labelSeparator}{venueName}");
        if (!string.IsNullOrWhiteSpace(SelectedDataset?.Name))
            infoParts.Add($"{Resources.Seating_MemberData}{labelSeparator}{SelectedDataset.Name}");
        infoParts.Add($"{Resources.Seating_ExportPerspective}{labelSeparator}{viewLabel}");
        infoParts.Add($"{Resources.Seating_ExportGeneratedAt}{labelSeparator}{now:yyyy-MM-dd HH:mm:ss}");

        return new ExportOptions
        {
            Format = format,
            IncludeMetadata = true,
            Perspective = perspective,
            HeaderTitle = $"{Resources.Seating_ExportChartTitle}  {now:yyyy-MM-dd HH:mm}",
            HeaderSubtitle = string.Join(Resources.Seating_ExportInfoSeparator, infoParts),
            FooterNote = $"By SeatFlow v{VersionInfo.Version}",
            Texts = new ExportTexts
            {
                SeatingChart = Resources.Seating_ExportChartTitle,
                Unassigned = Resources.Seating_TabUnassigned,
                Podium = Resources.Freeform_Podium,
                Door = Resources.Freeform_Door,
                DoorNumberFormat = Resources.Freeform_DoorFmt,
                LabelSeparator = labelSeparator,
                InfoSeparator = Resources.Seating_ExportInfoSeparator,
                DoorSeparator = Resources.Seating_ExportDoorSeparator,
            }
        };
    }

    private async Task ExportAsync(ExportFormat format, IReadOnlyList<FilePickerFileType> types, string suggestedName, LayoutPerspective perspective = LayoutPerspective.StudentView)
    {
        if (Interlocked.CompareExchange(ref _dialogLock, 1, 0) != 0) return;
        try
        {
            if (_workspace == null) return;

            if (_currentLayout?.LayoutType == LayoutType.Freeform)
            {
                await Dialog.ShowWarningAsync(Resources.Seating_UnsupportedExport,
                    Resources.Seating_UnsupportedExportMsg);
                return;
            }

            var baseName = Path.GetFileNameWithoutExtension(suggestedName);
            var ext = Path.GetExtension(suggestedName);
            var perspectiveLabel = perspective == LayoutPerspective.TeacherView
                ? Resources.Seating_TeacherView
                : Resources.Seating_StudentView;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            var fullSuggestedName = $"{baseName}_{perspectiveLabel}_{timestamp}{ext}";

            if (OperatingSystem.IsBrowser())
            {
                // WASM：导出 → 字节 → 浏览器下载（无文件系统）
                var webOk = await SafeExecuteAsync(async (ct) =>
                {
                    var options = BuildExportOptions(format, perspective);
                    var bytes = await _facade.ExportSeatingPlanBytesAsync(_workspace, _currentLayout, options, ct);
                    await _fileService.SaveFileBytesAsync(fullSuggestedName, bytes, types);
                    StatusMessage = string.Format(Resources.Seating_ExportedFmt, fullSuggestedName);
                }, ExportTimeout, Resources.Seating_ExportTitle);
                if (!webOk)
                    StatusMessage = Resources.Seating_ExportTimeout;
                return;
            }

            var file = await _fileService.SaveFileAsync(Resources.Seating_ExportTitle, types, fullSuggestedName);
            if (file == null) return;

            var filePath = file.Path.LocalPath;
            var ok = await SafeExecuteAsync(async (ct) =>
            {
                var options = BuildExportOptions(format, perspective);
                await _facade.ExportSeatingPlanAsync(_workspace, _currentLayout, filePath, options, ct);
                StatusMessage = string.Format(Resources.Seating_ExportedFmt, file.Name);
            }, ExportTimeout, Resources.Seating_ExportTitle);

            if (!ok)
            {
                try { File.Delete(filePath); } catch { /* ignore */ }
                StatusMessage = Resources.Seating_ExportTimeout;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "导出文件对话框取消或异常");
        }
        finally
        {
            await Task.Delay(150);
            Interlocked.Exchange(ref _dialogLock, 0);
        }
    }

    /// <summary>
    /// 将 StrategyMessage 的 MessageKey + Args 格式化为可读消息。
    /// Args 中的学生 ID 会被解析为姓名。
    /// </summary>
    private static string FormatStrategyMessage(StrategyMessage m,
        Dictionary<string, string> studentNames,
        Dictionary<string, string> templates)
    {
        var template = templates.TryGetValue(m.MessageKey, out var t) ? t : m.MessageKey;
        var resolved = m.Args.Select(a =>
        {
            if (a is not string s) return a;
            // 拆分逗号分隔的 ID 列表，逐个解析为姓名
            return string.Join(", ",
                s.Split(',').Select(part =>
                    studentNames.TryGetValue(part.Trim(), out var n) ? n : part.Trim()));
        }).ToArray();
        try { return string.Format(template, resolved); }
        catch { return template; }
    }
}

public partial class HistoryEntry(string description, Dictionary<string, string> assignments) : ObservableObject
{
    public string Description { get; set; } = description;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public Dictionary<string, string> Assignments { get; set; } = assignments;
    public bool IsCurrent { get; set; }
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public class StrategyMessageGroup
{
    public StrategyMessageSeverity Severity { get; init; }
    public string Title { get; init; } = "";
    public ObservableCollection<StrategyMessageItem> Messages { get; init; } = [];
}

public class StrategyMessageItem
{
    public string StrategyName { get; init; } = "";
    public string Message { get; init; } = "";
    public StrategyMessageSeverity Severity { get; init; }
}
