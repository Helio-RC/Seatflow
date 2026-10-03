using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.DomainServices;
using SeatFlow.Core.Models;
using SeatFlow.Infrastructure.Layouts;
using SeatFlow.Presentation.Avalonia.Controls;
using SeatFlow.Core.Utilities;
using SeatFlow.Presentation.Avalonia.Helpers;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 「会场与布局」页面 ViewModel（M2 重构）：
/// - 合并原「自由点管理」页：Grid / Polar / Freeform 三种布局在同一页编辑；
/// - 参数变更经 120ms 去抖后单次重算预览（<see cref="PreviewRevision"/> + ReactiveUI Throttle）；
/// - 生命周期实现 <see cref="IPageLifecycle"/>（构造器不再 fire-and-forget 加载，由 View Loaded/Unloaded 桥接驱动）；
/// - 脏检查统一走 <see cref="Services.DirtyTracker"/>；
/// - 预览由编辑参数构建 <see cref="SeatLayoutSnapshot"/>，交给 M1 自绘 <see cref="SeatingCanvas"/> 渲染（不再用 ItemsControl+Canvas）。
/// </summary>
public partial class VenueConfigurationViewModel : ViewModelBase, IPageLifecycle, IFileDropHandler, IGuideSeedTarget
{
    private readonly IApplicationFacade _facade;
    private readonly IFileService _fileService;
    private readonly IDialogGate _dialogGate;
    private readonly ILogger<VenueConfigurationViewModel> _logger;

    public string Title { get; } = Resources.Venue_Title;

    /// <summary>统一脏检查（载入/保存后 MarkClean，编辑时 Update）。</summary>
    public DirtyTracker DirtyTracker { get; } = new();

    public bool IsDirty => DirtyTracker.IsDirty;

    // ═══════════════════════════════════════════════
    // 会场列表
    // ═══════════════════════════════════════════════

    [ObservableProperty]
    public partial ObservableCollection<VenueItem> VenueItems { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVenue))]
    [NotifyPropertyChangedFor(nameof(SelectedVenueId))]
    [NotifyPropertyChangedFor(nameof(CanSaveVenue))]
    public partial VenueItem? SelectedVenueItem { get; set; }

    public string? SelectedVenueId => SelectedVenueItem?.Id;
    public bool HasSelectedVenue => SelectedVenueItem != null;

    [ObservableProperty]
    public partial string LayoutName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridSelected))]
    [NotifyPropertyChangedFor(nameof(IsPolarSelected))]
    [NotifyPropertyChangedFor(nameof(IsFreeformSelected))]
    [NotifyPropertyChangedFor(nameof(IsDoorPanelVisible))]
    [NotifyPropertyChangedFor(nameof(CanSaveVenue))]
    [NotifyPropertyChangedFor(nameof(IsFreeformEmptyState))]
    public partial LayoutType SelectedLayoutType { get; set; } = LayoutType.Grid;

    public bool IsGridSelected => SelectedLayoutType == LayoutType.Grid;
    public bool IsPolarSelected => SelectedLayoutType == LayoutType.Polar;
    public bool IsFreeformSelected => SelectedLayoutType == LayoutType.Freeform;

    /// <summary>门配置面板（Grid/Polar 共用；Freeform 的门在坐标表中维护）。</summary>
    public bool IsDoorPanelVisible => !IsFreeformSelected;

    /// <summary>自由点布局会场的布局类型锁定（与旧实现一致）。</summary>
    [ObservableProperty]
    public partial bool IsFreeformVenue { get; set; }

    public bool CanChangeLayoutType => !IsFreeformVenue;

    partial void OnIsFreeformVenueChanged(bool value) => OnPropertyChanged(nameof(CanChangeLayoutType));

    partial void OnSelectedLayoutTypeChanged(LayoutType value)
    {
        // 切换布局页签时把在途禁用选择落盘到编辑器状态，避免草稿丢失
        if (IsPickingDisabledSeats)
        {
            CommitPendingDisabledToggles();
            PreviewRevision++;
        }
    }

    // ── Grid 基础参数 ──
    [ObservableProperty]
    public partial int GridRows { get; set; } = 5;

    [ObservableProperty]
    public partial int GridColumns { get; set; } = 8;

    [ObservableProperty]
    public partial double GridHorizontalSpacing { get; set; } = 40;

    [ObservableProperty]
    public partial double GridVerticalSpacing { get; set; } = 36;

    [ObservableProperty]
    public partial double GridOriginX { get; set; } = 200;

    [ObservableProperty]
    public partial double GridOriginY { get; set; } = 200;

    // ── Grid 桌面配置 ──
    [ObservableProperty]
    public partial int GridSeatsPerDesk { get; set; } = 2;

    [ObservableProperty]
    public partial double GridIntraDeskSpacing { get; set; } = 12;

    [ObservableProperty]
    public partial double GridInterDeskSpacing { get; set; } = 32;

    // ── Grid 过道配置 ──
    [ObservableProperty]
    public partial string GridAisleAfterColumns { get; set; } = "";

    [ObservableProperty]
    public partial string GridAisleAfterRows { get; set; } = "";

    [ObservableProperty]
    public partial double GridAisleWidth { get; set; } = 60;

    [ObservableProperty]
    public partial ObservableCollection<AisleOption> AisleColumnOptions { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<AisleOption> AisleRowOptions { get; set; } = [];

    // ── Grid 教室特征 ──
    [ObservableProperty]
    public partial int GridFrontRowCount { get; set; } = 1;

    [ObservableProperty]
    public partial bool GridHasPodium { get; set; } = true;

    [ObservableProperty]
    public partial bool GridHasFrontDoor { get; set; }

    [ObservableProperty]
    public partial double GridPodiumWidth { get; set; } = 60;

    [ObservableProperty]
    public partial double GridPodiumHeight { get; set; } = 40;

    // ── Grid 每列行数 & 禁用座位 ──
    [ObservableProperty]
    public partial string GridColumnRowCountsSpec { get; set; } = "";

    [ObservableProperty]
    public partial string GridEmptyPositionsSpec { get; set; } = "";

    /// <summary>每列行数输入项（数量同步 <see cref="GridColumns"/>；留空 = 沿用「行数」）。</summary>
    [ObservableProperty]
    public partial ObservableCollection<ColumnRowCountOption> ColumnRowCountOptions { get; set; } = [];

    /// <summary>禁用座位拾取模式（Grid/Polar 共用；选择草稿在预览点击中维护，保存时才写入 spec）。</summary>
    [ObservableProperty]
    public partial bool IsPickingDisabledSeats { get; set; }

    /// <summary>「清除全部禁用座位」是否可用（当前布局存在已禁用座位或待选草稿）。</summary>
    public bool CanClearDisabledSeats => SelectedLayoutType switch
    {
        LayoutType.Grid => !string.IsNullOrEmpty(GridEmptyPositionsSpec) || _pendingGridDisabledToggles.Count > 0,
        LayoutType.Polar => !string.IsNullOrEmpty(PolarEmptyPositionsSpec) || _pendingPolarDisabledToggles.Count > 0,
        _ => false,
    };

    // ── 门配置（支持多门、自定义位置；Grid/Polar 使用） ──
    [ObservableProperty]
    public partial ObservableCollection<DoorItem> DoorItems { get; set; } = [];

    // ── Polar 参数 ──
    [ObservableProperty]
    public partial int PolarRings { get; set; } = 3;

    [ObservableProperty]
    public partial int PolarSeatsPerRing { get; set; } = 12;

    [ObservableProperty]
    public partial double PolarRadiusStep { get; set; } = 40;

    [ObservableProperty]
    public partial double PolarStartAngle { get; set; } = 0;

    [ObservableProperty]
    public partial double PolarEndAngle { get; set; } = 360;

    [ObservableProperty]
    public partial double PolarOriginX { get; set; } = 200;

    [ObservableProperty]
    public partial double PolarOriginY { get; set; } = 200;

    [ObservableProperty]
    public partial string PolarRingSeatCountsSpec { get; set; } = "";

    [ObservableProperty]
    public partial string PolarEmptyPositionsSpec { get; set; } = "";

    [ObservableProperty]
    public partial bool PolarHasPodium { get; set; } = true;

    [ObservableProperty]
    public partial double PolarPodiumRadius { get; set; } = 30;

    [ObservableProperty]
    public partial string PolarAisleRadialAngles { get; set; } = "";

    [ObservableProperty]
    public partial double PolarAisleRadialWidth { get; set; } = 5;

    [ObservableProperty]
    public partial string PolarAisleCircularRings { get; set; } = "";

    [ObservableProperty]
    public partial double PolarAisleCircularWidth { get; set; } = 20;

    [ObservableProperty]
    public partial int PolarFrontRowCount { get; set; } = 1;

    // ── 自由点（Freeform）数据 ──
    /// <summary>自由点坐标表（座位/讲台/门统一维护）。</summary>
    public ObservableCollection<FreeformPoint> Points { get; } = [];

    /// <summary>自由点坐标表是否为空。</summary>
    public bool HasPoints => Points.Count > 0;

    /// <summary>当前处于 Freeform 布局且尚无坐标点时显示空态提示。</summary>
    public bool IsFreeformEmptyState => IsFreeformSelected && Points.Count == 0;

    public string ElementCountDisplay => string.Format(Resources.Freeform_ElementCountFmt, Points.Count);

    // ── 预览 ──
    /// <summary>
    /// 预览快照（由编辑中的参数构建；交给 SeatingCanvas 单控件自绘）。
    /// 与旧实现不同：不再暴露 ItemsControl 用的座位/覆盖物集合。
    /// </summary>
    [ObservableProperty]
    public partial SeatLayoutSnapshot? PreviewSnapshot { get; set; }

    /// <summary>预览座位数（状态栏展示）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewSeatCountDisplay))]
    public partial int PreviewSeatCount { get; set; }

    public string PreviewSeatCountDisplay => string.Format(Resources.Venue_PreviewSeatsFmt, PreviewSeatCount);

    /// <summary>去抖期间的重算提示（可选轻量状态）。</summary>
    [ObservableProperty]
    public partial bool IsRecomputing { get; set; }

    /// <summary>
    /// 重算触发序号：所有编辑入口（属性/坐标点/门）统一递增它，
    /// 由 ReactiveUI Throttle(120ms) 归并为单次 <see cref="RecomputePreviewNow"/>。
    /// </summary>
    [ObservableProperty]
    public partial long PreviewRevision { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>待保存会场是否可保存：Freeform 需至少一个坐标点。</summary>
    public bool CanSaveVenue => HasSelectedVenue && (!IsFreeformSelected || Points.Count > 0);

    /// <summary>
    /// 首次加载完成信号。引导示例数据注入需等待它，避免随后加载覆盖演示会场
    /// （见 <c>OnboardingService.SeedPageDataAsync</c>）。
    /// </summary>
    public Task InitializationTask => _firstLoadTcs.Task;

    // ═══════════════════════════════════════════════
    // 内部状态
    // ═══════════════════════════════════════════════

    private readonly TaskCompletionSource _firstLoadTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>已加载过会场列表（页面缓存：再次进入不重复加载）。</summary>
    private bool _venuesLoaded;

    /// <summary>抑制加载/重置期间的状态跟踪（脏标记与预览触发）。</summary>
    private bool _suppressEditorTracking;

    private bool _suppressAutoLoad;
    private CancellationTokenSource? _selectVenueCts;
    private CancellationTokenSource? _refreshCts;

    /// <summary>已加载会场的座位位置→ID 映射，用于保存时保留旧 ID 避免快照失效。</summary>
    private Dictionary<(int Row, int Col), string>? _existingGridSeatMap;

    /// <summary>Polar 会场的 (环号, 角度) → ID 映射。</summary>
    private Dictionary<(int Ring, double Angle), string>? _existingPolarSeatMap;

    /// <summary>禁用座位拾取草稿：Grid (行,列) / Polar (环, 角度两位小数)；保存前不写 spec、不标脏。</summary>
    private readonly HashSet<(int Row, int Column)> _pendingGridDisabledToggles = [];

    private readonly HashSet<(int Ring, double Angle)> _pendingPolarDisabledToggles = [];

    /// <summary>自由点集合内容修订号（脏检查用；点属性/集合变化时自增）。</summary>
    private long _freeformPointsRevision;

    /// <summary>门集合内容修订号。</summary>
    private long _doorRevision;

    private readonly List<IDisposable> _subscriptions = [];

    /// <summary>参与编辑器状态跟踪的属性名（单一入口，替代 40 个 OnXxxChanged）。</summary>
    private static readonly HashSet<string> EditorPropertyNames = new(StringComparer.Ordinal)
    {
        nameof(GridRows), nameof(GridColumns),
        nameof(GridHorizontalSpacing), nameof(GridVerticalSpacing),
        nameof(GridOriginX), nameof(GridOriginY),
        nameof(GridSeatsPerDesk), nameof(GridIntraDeskSpacing), nameof(GridInterDeskSpacing),
        nameof(GridAisleAfterColumns), nameof(GridAisleAfterRows), nameof(GridAisleWidth),
        nameof(GridFrontRowCount), nameof(GridHasPodium), nameof(GridPodiumWidth), nameof(GridPodiumHeight),
        nameof(GridColumnRowCountsSpec), nameof(GridEmptyPositionsSpec),
        nameof(PolarRings), nameof(PolarSeatsPerRing), nameof(PolarRadiusStep),
        nameof(PolarStartAngle), nameof(PolarEndAngle),
        nameof(PolarOriginX), nameof(PolarOriginY), nameof(PolarRingSeatCountsSpec),
        nameof(PolarEmptyPositionsSpec), nameof(PolarHasPodium), nameof(PolarPodiumRadius),
        nameof(PolarAisleRadialAngles), nameof(PolarAisleRadialWidth),
        nameof(PolarAisleCircularRings), nameof(PolarAisleCircularWidth), nameof(PolarFrontRowCount),
        nameof(SelectedLayoutType),
    };

    public VenueConfigurationViewModel(
        IApplicationFacade facade,
        IFileService fileService,
        IDialogGate dialogGate,
        IDialogService dialog,
        ILogger<VenueConfigurationViewModel>? logger = null) : base(dialog, logger)
    {
        _facade = facade;
        _fileService = fileService;
        _dialogGate = dialogGate;
        _logger = logger ?? NullLogger<VenueConfigurationViewModel>.Instance;

        // 构造器只做纯内存初始化（不再 fire-and-forget 加载；加载由 OnEnterAsync 驱动）
        RegenerateAisleOptions();
        SubscribeToDoorCollection(DoorItems);
        SubscribeToPointsCollection();

        // 单一去抖入口：任意编辑 → 序号递增 → 120ms 合并 → 单次重算
        _subscriptions.Add(
            this.WhenAnyValue(x => x.PreviewRevision)
                .Throttle(TimeSpan.FromMilliseconds(120), RxSchedulers.MainThreadScheduler)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .SubscribeSafe(
                    _ => RecomputePreviewNow(),
                    ex => _logger.LogWarning(ex, "预览重算调度失败")));
    }

    // ═══════════════════════════════════════════════
    // IPageLifecycle
    // ═══════════════════════════════════════════════

    public async Task OnEnterAsync(CancellationToken ct)
    {
        if (_venuesLoaded) return;

        try
        {
            await RefreshVenueListAsync(ct);
            _venuesLoaded = true;
        }
        catch (OperationCanceledException)
        {
            // 离开页面取消，保持未加载状态以便下次重试
        }
        finally
        {
            _firstLoadTcs.TrySetResult();
        }
    }

    public Task OnLeaveAsync()
    {
        // 取消在途加载（在途会场选择 + 列表刷新）
        _selectVenueCts?.Cancel();
        _refreshCts?.Cancel();
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════
    // IGuideSeedTarget（M5：引导演示注入/清理下沉到页面）
    // ═══════════════════════════════════════════════

    /// <summary>新建一个演示会场（不落盘），供引导展示布局编辑区。</summary>
    public void SeedGuideData()
    {
        // 已等待首次加载完成，此处创建的会场不会被异步加载覆盖
        NewVenueCommand.Execute(null);
        LayoutName = "演示教室";
        StatusMessage = "已创建演示会场（演示数据）";
    }

    /// <summary>清空演示会场状态（与旧 OnboardingService.ClearPageData 行为一致）。</summary>
    public void ClearGuideData()
    {
        VenueItems.Clear();
        SelectedVenueItem = null;
        // M2：预览改为 SeatingCanvas 快照，清空快照即可
        PreviewSnapshot = null;
        StatusMessage = string.Empty;
    }

    // ═══════════════════════════════════════════════
    // 会场列表
    // ═══════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadVenueList()
    {
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        try
        {
            await RefreshVenueListAsync(_refreshCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshVenueListAsync(CancellationToken ct)
    {
        try
        {
            var summaries = await _facade.ListVenueSummariesAsync(ct);
            ct.ThrowIfCancellationRequested();
            var items = summaries
                .OrderBy(s => s.Name, NaturalStringComparer.Instance)
                .ThenBy(s => s.Id, StringComparer.Ordinal)
                .Select(s => new VenueItem(s.Id, s.Name))
                .ToList();

            var selectedId = SelectedVenueItem?.Id;
            _suppressAutoLoad = true;
            VenueItems = new ObservableCollection<VenueItem>(items);
            SelectedVenueItem = items.FirstOrDefault(v => v.Id == selectedId);
            _suppressAutoLoad = false;

            _logger.LogInformation("已加载 {Count} 个会场", items.Count);
            StatusMessage = string.Format(Resources.Venue_VenuesLoadedFmt, items.Count);
        }
        catch (OperationCanceledException)
        {
            _suppressAutoLoad = false;
            throw;
        }
        catch (Exception ex)
        {
            _suppressAutoLoad = false;
            _logger.LogError(ex, "加载会场列表失败");
            await Dialog.ShowErrorAsync(Resources.Common_OperationFailed, ex.Message);
        }
    }

    [RelayCommand]
    private void NewVenue()
    {
        _selectVenueCts?.Cancel();
        _suppressAutoLoad = true;
        _suppressEditorTracking = true;
        try
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            var item = new VenueItem(id, string.Format(Resources.Venue_NewVenueFmt, id));
            LayoutName = item.Name;
            IsFreeformVenue = false;
            SelectedLayoutType = LayoutType.Grid;
            _existingGridSeatMap = null;
            _existingPolarSeatMap = null;
            ResetParameters();
            DirtyTracker.MarkClean(BuildDirtySnapshot());
            VenueItems.Add(item);
            SelectedVenueItem = item;
        }
        finally
        {
            _suppressEditorTracking = false;
            _suppressAutoLoad = false;
        }

        RecomputePreviewNow();
        StatusMessage = Resources.Venue_New;
    }

    [RelayCommand]
    private async Task RenameVenue()
    {
        if (SelectedVenueItem == null) return;
        var item = SelectedVenueItem;

        var (confirmed, input) = await Dialog.ShowInputAsync(
            Resources.Venue_RenameTitle,
            string.Format(Resources.Venue_RenamePrompt, item.Name),
            item.Name);
        if (!confirmed) return;

        var newName = input?.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        await SafeExecuteAsync(async () =>
        {
            var wasDirty = DirtyTracker.IsDirty;
            await _facade.RenameVenueAsync(item.Id, newName);

            // 重命名立即持久化：同步编辑器名称；若重命名前没有其他未保存修改，清除脏状态
            LayoutName = newName;
            if (!wasDirty)
                DirtyTracker.MarkClean(BuildDirtySnapshot());

            await RefreshVenueListAsync(CancellationToken.None);
            StatusMessage = string.Format(Resources.Venue_RenamedFmt, newName);
        }, Resources.Venue_RenameFailed);
    }

    [RelayCommand]
    private async Task DeleteVenue()
    {
        if (SelectedVenueItem == null) return;
        var item = SelectedVenueItem;
        var confirmed = await Dialog.ShowConfirmAsync(Resources.Venue_DeleteConfirm,
            string.Format(Resources.Venue_DeleteConfirmMsgFmt, item.Name));
        if (!confirmed) return;

        await SafeExecuteAsync(async () =>
        {
            await _facade.DeleteVenueAsync(item.Id);
            _logger.LogInformation("会场已删除: {VenueId}", item.Id);
            ClearVenueState();
            await RefreshVenueListAsync(CancellationToken.None);
            StatusMessage = string.Format(Resources.Venue_DeletedFmt, item.Name);
        }, Resources.Venue_DeleteFailed);
    }

    private async Task SelectVenueAsync(VenueItem item, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            ResetDisabledSeatsPicking();
            var layout = await _facade.LoadVenueAsync(item.Id);
            if (ct.IsCancellationRequested) return;
            if (layout == null)
            {
                StatusMessage = string.Format(Resources.Venue_LoadFailedFmt, item.Name);
                return;
            }

            _suppressEditorTracking = true;
            try
            {
                LayoutName = layout.Name;
                SelectedLayoutType = layout.LayoutType;
                IsFreeformVenue = layout.LayoutType == LayoutType.Freeform;

                switch (layout.Metadata)
                {
                    case GridLayoutMetadata g:
                        _existingGridSeatMap = layout.Seats.OfType<GridSeat>()
                            .ToDictionary(s => (s.Row, s.Column), s => s.Id);
                        _existingPolarSeatMap = null;
                        PopulateGridFromMetadata(g);
                        RestoreObstaclesFromLayout(layout);
                        break;
                    case PolarLayoutMetadata p:
                        _existingPolarSeatMap = layout.Seats.OfType<PolarSeat>()
                            .ToDictionary(s => (s.Ring, Math.Round(s.AngleDegrees, 2)), s => s.Id);
                        _existingGridSeatMap = null;
                        PopulatePolarFromMetadata(p);
                        RestoreObstaclesFromLayout(layout);
                        break;
                    default:
                        _existingGridSeatMap = null;
                        _existingPolarSeatMap = null;
                        if (layout.LayoutType == LayoutType.Freeform)
                            PopulateFreeformFromLayout(layout);
                        break;
                }
            }
            finally
            {
                _suppressEditorTracking = false;
            }

            DirtyTracker.MarkClean(BuildDirtySnapshot());
            RefreshPointsState();
            RecomputePreviewNow();
            StatusMessage = string.Format(Resources.Venue_LoadedFmt, layout.Name, layout.Seats.Count);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载会场失败: {VenueId}", item.Id);
            await Dialog.ShowErrorAsync(Resources.Common_OperationFailed, ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveVenue()
    {
        if (SelectedVenueItem == null) return;
        var item = SelectedVenueItem;

        // Freeform 需名称与有效坐标（与旧自由点页一致）
        if (IsFreeformSelected)
        {
            if (string.IsNullOrWhiteSpace(LayoutName))
            {
                await Dialog.ShowWarningAsync(Resources.Data_SaveFailed, Resources.Freeform_EnterLayoutName);
                return;
            }

            var errors = ValidatePoints();
            if (errors.Count > 0)
            {
                await Dialog.ShowErrorAsync(Resources.Data_ValidationFailed,
                    string.Join('\n', errors.Take(10)));
                return;
            }
        }

        await SafeExecuteAsync(async () =>
        {
            CommitPendingDisabledToggles();
            var layout = BuildLayoutDefinition();
            await _facade.SaveVenueAsync(item.Id, layout);
            DirtyTracker.MarkClean(BuildDirtySnapshot());
            _logger.LogInformation("会场已保存: {VenueId} - {SeatCount} 座", item.Id, layout.Seats.Count);

            await RefreshVenueListAsync(CancellationToken.None);
            StatusMessage = string.Format(Resources.Venue_SavedFmt, layout.Name, layout.Seats.Count);
        }, Resources.Venue_SaveFailed);
    }

    [RelayCommand]
    private void SelectLayoutType(string type)
    {
        if (IsFreeformVenue) return;
        SelectedLayoutType = type switch
        {
            "Polar" => LayoutType.Polar,
            "Freeform" => LayoutType.Freeform,
            _ => LayoutType.Grid,
        };
        RefreshPointsState();
    }

    [RelayCommand]
    private void AddDoor()
    {
        double dx = GridOriginX - 50;
        double dy = GridOriginY - 20;
        DoorItems.Add(new DoorItem(dx, dy, string.Format(Resources.Venue_DoorFmt, DoorItems.Count + 1)));
    }

    [RelayCommand]
    private void RemoveDoor(DoorItem door)
    {
        DoorItems.Remove(door);
    }

    // ═══════════════════════════════════════════════
    // 禁用座位拾取（Grid / Polar）
    // ═══════════════════════════════════════════════

    /// <summary>入口按钮：未拾取时进入选择模式，拾取中再次点击 = 保存并退出。</summary>
    [RelayCommand]
    private void ToggleDisabledSeatsPicking()
    {
        if (IsPickingDisabledSeats)
        {
            CommitPendingDisabledToggles();
            PreviewRevision++;
            return;
        }

        ClearPendingDisabledToggles();
        IsPickingDisabledSeats = true;
        OnPropertyChanged(nameof(CanClearDisabledSeats));
    }

    /// <summary>预览区保存按钮：应用草稿并退出选择模式。</summary>
    [RelayCommand]
    private void SaveDisabledSeatsPicking()
    {
        if (!IsPickingDisabledSeats) return;
        CommitPendingDisabledToggles();
        PreviewRevision++;
    }

    /// <summary>清除当前布局的全部禁用座位（含未保存草稿），不弹确认。</summary>
    [RelayCommand]
    private void ClearDisabledSeats()
    {
        switch (SelectedLayoutType)
        {
            case LayoutType.Grid:
                GridEmptyPositionsSpec = "";
                _pendingGridDisabledToggles.Clear();
                break;
            case LayoutType.Polar:
                PolarEmptyPositionsSpec = "";
                _pendingPolarDisabledToggles.Clear();
                break;
        }

        OnPropertyChanged(nameof(CanClearDisabledSeats));
        PreviewRevision++;
    }

    /// <summary>预览座位点击：拾取模式下 toggle 对应布局的禁用草稿。</summary>
    public void OnPreviewSeatClicked(SeatVisual seat)
    {
        if (!IsPickingDisabledSeats) return;

        switch (SelectedLayoutType)
        {
            case LayoutType.Grid when seat.Row is { } row && seat.Column is { } column:
                TogglePending(_pendingGridDisabledToggles, (row, column));
                break;
            case LayoutType.Polar when seat.Ring is { } ring && seat.AngleDegrees is { } angle:
                TogglePending(_pendingPolarDisabledToggles, (ring, Math.Round(angle, 2)));
                break;
            default:
                return;
        }

        OnPropertyChanged(nameof(CanClearDisabledSeats));
        PreviewRevision++;
    }

    private static void TogglePending<T>(HashSet<T> set, T key) where T : notnull
    {
        if (!set.Add(key)) set.Remove(key);
    }

    private void ClearPendingDisabledToggles()
    {
        _pendingGridDisabledToggles.Clear();
        _pendingPolarDisabledToggles.Clear();
    }

    private void ResetDisabledSeatsPicking()
    {
        IsPickingDisabledSeats = false;
        ClearPendingDisabledToggles();
        OnPropertyChanged(nameof(CanClearDisabledSeats));
    }

    /// <summary>把草稿（已禁用 ⊕ 待选）写入 spec 并退出选择模式。</summary>
    private void CommitPendingDisabledToggles()
    {
        if (_pendingGridDisabledToggles.Count > 0)
        {
            var effective = GetEffectiveGridEmptyPositions();
            GridEmptyPositionsSpec = string.Join(";", effective.Select(p => $"{p.Row},{p.Column}"));
        }

        if (_pendingPolarDisabledToggles.Count > 0)
        {
            var effective = GetEffectivePolarEmptyPositions();
            PolarEmptyPositionsSpec = string.Join(";", effective.Select(p => $"{p.Ring},{p.AngleDegrees:F2}"));
        }

        IsPickingDisabledSeats = false;
        ClearPendingDisabledToggles();
        OnPropertyChanged(nameof(CanClearDisabledSeats));
    }

    /// <summary>已禁用集合与草稿的对称差（预览渲染与保存共用）。</summary>
    private List<GridPosition> GetEffectiveGridEmptyPositions()
    {
        var committed = FilterGridEmptyPositions(
            ParseGridEmptyPositions(GridEmptyPositionsSpec), GridColumns, GridRows,
            ParseIntList(GridColumnRowCountsSpec));
        if (_pendingGridDisabledToggles.Count == 0) return committed;

        var set = new HashSet<(int Row, int Column)>(committed.Select(p => (p.Row, p.Column)));
        foreach (var key in _pendingGridDisabledToggles)
        {
            if (!set.Add(key)) set.Remove(key);
        }

        // 草稿键可能因拾取期间修改行列数而越界，提交前统一过滤
        return FilterGridEmptyPositions(
            [.. set.Select(k => new GridPosition { Row = k.Row, Column = k.Column })
                .OrderBy(p => p.Row).ThenBy(p => p.Column)],
            GridColumns, GridRows, ParseIntList(GridColumnRowCountsSpec));
    }

    private List<PolarRingAngle> GetEffectivePolarEmptyPositions()
    {
        var committed = FilterPolarEmptyPositions(
            ParsePolarEmptyPositions(PolarEmptyPositionsSpec),
            ParseIntList(PolarRingSeatCountsSpec), PolarRings);
        if (_pendingPolarDisabledToggles.Count == 0) return committed;

        var set = new HashSet<(int Ring, double Angle)>(
            committed.Select(p => (p.Ring, Math.Round(p.AngleDegrees, 2))));
        foreach (var key in _pendingPolarDisabledToggles)
        {
            if (!set.Add(key)) set.Remove(key);
        }

        // 草稿键可能因拾取期间修改环数而越界，提交前统一过滤
        return FilterPolarEmptyPositions(
            [.. set.Select(k => new PolarRingAngle { Ring = k.Ring, AngleDegrees = k.Angle })],
            ParseIntList(PolarRingSeatCountsSpec), PolarRings);
    }

    // ═══════════════════════════════════════════════
    // 自由点（Freeform）操作
    // ═══════════════════════════════════════════════

    [RelayCommand]
    private void AddPoint()
    {
        Points.Add(new FreeformPoint(0, 0));
        RefreshIndices();
        StatusMessage = string.Format(Resources.Freeform_PointAddedFmt, Points.Count);
    }

    [RelayCommand]
    private void DeletePoint(FreeformPoint point)
    {
        Points.Remove(point);
        RefreshIndices();
        StatusMessage = string.Format(Resources.Freeform_PointCountFmt, Points.Count);
    }

    [RelayCommand]
    private void ClearPoints()
    {
        Points.Clear();
        StatusMessage = Resources.Freeform_PointsCleared;
    }

    [RelayCommand]
    private void Unload()
    {
        Points.Clear();
        StatusMessage = Resources.Freeform_UnloadedHint;
    }

    [RelayCommand]
    private async Task ExportTemplate()
    {
        await _dialogGate.RunAsync(async () =>
        {
            const string templateCsv =
                "X,Y,Type,GroupId,Row,Column\n" +
                "100,100,Seat,1,1,1\n" +
                "200,100,Seat,1,1,2\n" +
                "300,100,Seat,2,2,1\n" +
                "100,200,Seat,2,2,2\n" +
                "200,200,Seat,3,3,1\n" +
                "300,200,Seat,3,3,2\n" +
                "200,50,Podium,,,\n" +
                "400,150,Door,,,\n";
            FilePickerFileType[] types = [new(Resources.Data_CSVFile) { Patterns = ["*.csv"] }];

            if (OperatingSystem.IsBrowser())
            {
                // WASM：模板文本 → 字节 → 浏览器下载
                await _fileService.SaveFileBytesAsync(
                    Resources.Freeform_CSVTemplate,
                    System.Text.Encoding.UTF8.GetBytes(templateCsv),
                    types);
                StatusMessage = Resources.Data_TemplateSaved;
                return;
            }

            IStorageFile? tmplFile;
            try
            {
                tmplFile = await _fileService.SaveFileAsync(
                    Resources.Freeform_SaveTemplate,
                    types,
                    Resources.Freeform_CSVTemplate);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "文件对话框取消或异常: 导出模板");
                return;
            }
            if (tmplFile == null) return;

            var file = tmplFile;
            await SafeExecuteAsync(async () =>
            {
                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(templateCsv);
                StatusMessage = Resources.Data_TemplateSaved;
            }, Resources.Data_TemplateSaveFailed);
        });
    }

    [RelayCommand]
    private async Task ImportCsv()
    {
        await _dialogGate.RunAsync(async () =>
        {
            string? csvPath;
            try
            {
                csvPath = await _fileService.OpenFilePathAsync(
                    Resources.Freeform_ImportCSV,
                    [new(Resources.Data_CSVFile) { Patterns = ["*.csv"] }]);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "文件对话框取消或异常: 导入CSV");
                return;
            }
            if (csvPath == null) return;

            await ImportCsvCoreAsync(csvPath, Path.GetFileName(csvPath));
        });
    }

    /// <summary>从指定路径导入 CSV 自由布局（跳过文件对话框）。</summary>
    private async Task ImportCsvCoreAsync(string filePath, string? displayName = null)
    {
        if (!await ConfirmImportConflictAsync()) return;

        await SafeExecuteAsync(async () =>
        {
            using var reader = new StreamReader(filePath);
            var pts = new List<FreeformPoint>();
            var lineNum = 0;
            while (await reader.ReadLineAsync() is { } line)
            {
                lineNum++;
                if (lineNum == 1) continue;
                var parts = line.Split(',');
                if (parts.Length >= 2 &&
                    double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x) &&
                    double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var y))
                {
                    var pt = new FreeformPoint(x, y);
                    if (parts.Length >= 3)
                        pt.ElementType = parts[2].Trim() switch
                        {
                            "Podium" => (int)FreeformElementType.Podium,
                            "Door" => (int)FreeformElementType.Door,
                            _ => (int)FreeformElementType.Seat
                        };
                    if (parts.Length >= 4 && int.TryParse(parts[3].Trim(), out var gid))
                        pt.GroupId = gid;
                    if (parts.Length >= 5 && int.TryParse(parts[4].Trim(), out var row))
                        pt.Row = row;
                    if (parts.Length >= 6 && int.TryParse(parts[5].Trim(), out var col))
                        pt.Column = col;
                    pts.Add(pt);
                }
            }

            ReplacePoints(pts);
            LayoutName = displayName?.Replace(".csv", "") ?? Path.GetFileNameWithoutExtension(filePath);
            StatusMessage = string.Format(Resources.Freeform_ImportedPtsFmt, pts.Count);
        }, Resources.Freeform_ImportFailed);
    }

    [RelayCommand]
    private async Task ImportJson()
    {
        await _dialogGate.RunAsync(async () =>
        {
            string? jsonPath;
            try
            {
                jsonPath = await _fileService.OpenFilePathAsync(
                    Resources.Freeform_ImportJSON,
                    [new(Resources.Data_JSONFile) { Patterns = ["*.json"] }]);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "文件对话框取消或异常: 导入JSON");
                return;
            }
            if (jsonPath == null) return;

            await ImportJsonCoreAsync(jsonPath);
        });
    }

    /// <summary>从指定路径导入 JSON 自由布局（跳过文件对话框）。</summary>
    private async Task ImportJsonCoreAsync(string filePath)
    {
        if (!await ConfirmImportConflictAsync()) return;

        await SafeExecuteAsync(async () =>
        {
            await using var stream = File.OpenRead(filePath);
            var layout = await System.Text.Json.JsonSerializer.DeserializeAsync<ClassroomLayoutDefinition>(stream);
            if (layout == null) return;

            var pts = new List<FreeformPoint>();
            foreach (var s in layout.Seats.OfType<FreeformSeat>())
            {
                int? groupId = null;
                if (!string.IsNullOrEmpty(s.LogicalGroup) && s.LogicalGroup.StartsWith('G')
                    && int.TryParse(s.LogicalGroup[1..], out var gid))
                    groupId = gid;
                pts.Add(new FreeformPoint(s.X, s.Y, s.Id)
                {
                    ElementType = (int)FreeformElementType.Seat,
                    GroupId = groupId,
                    Row = s.Row,
                    Column = s.Column
                });
            }
            foreach (var obs in layout.Obstacles)
            {
                var et = obs.Type == "Podium" ? (int)FreeformElementType.Podium
                       : obs.Type == "Door" ? (int)FreeformElementType.Door
                       : (int)FreeformElementType.Seat;
                pts.Add(new FreeformPoint(obs.X, obs.Y)
                {
                    ElementType = et,
                    Width = obs.Width,
                    Height = obs.Height
                });
            }

            ReplacePoints(pts);
            LayoutName = layout.Name;
            StatusMessage = string.Format(Resources.Freeform_ImportedFmt, pts.Count);
        }, Resources.Freeform_ImportFailed);
    }

    /// <summary>导入前冲突确认（保持旧自由点页行为）。返回 false 表示取消导入。</summary>
    private async Task<bool> ConfirmImportConflictAsync()
    {
        if (Points.Count == 0) return true;

        var choice = await Dialog.ShowMultiOptionAsync(Resources.Freeform_ImportTitle,
            string.Format(Resources.Freeform_ImportMsgFmt, Points.Count),
            Resources.Freeform_UnloadAndImport, Resources.Freeform_Overwrite, Resources.Common_Cancel);
        if (choice == null || choice == 2) return false;
        if (choice == 0)
        {
            Unload();
        }
        return true;
    }

    private void ReplacePoints(List<FreeformPoint> pts)
    {
        Points.Clear();
        foreach (var p in pts)
            Points.Add(p);
        RefreshIndices();
    }

    private void RefreshIndices()
    {
        for (int i = 0; i < Points.Count; i++)
            Points[i].DisplayIndex = i + 1;
    }

    private List<string> ValidatePoints()
    {
        var errors = new List<string>();
        var seen = new HashSet<(double, double)>();

        for (int i = 0; i < Points.Count; i++)
        {
            var p = Points[i];
            var n = i + 1;

            if (double.IsNaN(p.X) || double.IsInfinity(p.X))
                errors.Add(string.Format(Resources.Freeform_RowXInvalidFmt, n));
            if (double.IsNaN(p.Y) || double.IsInfinity(p.Y))
                errors.Add(string.Format(Resources.Freeform_RowYInvalidFmt, n));
            if (p.Y < 0)
                errors.Add(string.Format(Resources.Freeform_RowYNegativeFmt, n, p.Y));

            if (p.ElementType == (int)FreeformElementType.Seat)
            {
                var key = (p.X, p.Y);
                if (seen.Contains(key))
                    errors.Add(string.Format(Resources.Freeform_DuplicatePointFmt, n, p.X, p.Y));
                seen.Add(key);
            }
        }

        return errors;
    }

    // ═══════════════════════════════════════════════
    // IFileDropHandler（拖放 .csv/.json 到本页 → 自由点导入）
    // ═══════════════════════════════════════════════

    IReadOnlyList<string> IFileDropHandler.AcceptedFileExtensions { get; } = [".csv", ".json"];

    async Task<bool> IFileDropHandler.HandleFileDropAsync(IReadOnlyList<string> filePaths, CancellationToken ct)
    {
        if (filePaths.Count == 0) return false;

        if (SelectedLayoutType != LayoutType.Freeform)
        {
            await Dialog.ShowWarningAsync(Resources.Freeform_ImportTitle, Resources.Venue_FreeformHint);
            return false;
        }

        var handled = await _dialogGate.RunAsync(async () =>
        {
            var filePath = filePaths[0];
            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".csv")
                await ImportCsvCoreAsync(filePath);
            else if (ext == ".json")
                await ImportJsonCoreAsync(filePath);
        });

        return handled;
    }

    // ═══════════════════════════════════════════════
    // 预览构建（编辑参数 → SeatLayoutSnapshot）
    // ═══════════════════════════════════════════════

    /// <summary>去抖后的单次重算入口。</summary>
    private void RecomputePreviewNow()
    {
        IsRecomputing = false;

        // 未选择会场时不展示任何预览（构造期的初始订阅也会走到这里）
        if (SelectedVenueItem is null)
        {
            PreviewSnapshot = null;
            PreviewSeatCount = 0;
            return;
        }

        try
        {
            // 行/列/每桌人数变化会改变过道选项集合；这里（去抖后）做增量同步，
            // 避免每次数值步进都重建整表 CheckBox 控件（WASM 下曾是数百 ms/键的瓶颈）。
            if (SelectedLayoutType == LayoutType.Grid)
            {
                RegenerateAisleOptions();
                RegenerateColumnRowCountOptions();
            }

            PreviewSnapshot = BuildPreviewSnapshot();
            PreviewSeatCount = PreviewSnapshot?.Seats.Count ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "会场预览重算失败");
            PreviewSnapshot = SeatLayoutSnapshot.Empty;
            PreviewSeatCount = 0;
        }
    }

    private SeatLayoutSnapshot BuildPreviewSnapshot()
    {
        var seats = new List<SeatVisual>();
        var overlays = new List<BoardOverlay>();

        switch (SelectedLayoutType)
        {
            case LayoutType.Grid:
                BuildGridPreview(seats, overlays);
                break;
            case LayoutType.Polar:
                BuildPolarPreview(seats, overlays);
                break;
            case LayoutType.Freeform:
                BuildFreeformPreview(seats, overlays);
                break;
        }

        return NormalizeSnapshot(seats, overlays);
    }

    private void BuildGridPreview(List<SeatVisual> seats, List<BoardOverlay> overlays)
    {
        var meta = BuildGridMetadata(GetEffectiveGridEmptyPositions());
        // 与排座工作台共用同一视觉几何（0.8 同桌压缩 + 可读尺寸坐标放大），保证两处一致
        var geometry = GridVisualGeometryBuilder.Build(meta);

        foreach (var s in geometry.Seats)
        {
            bool isPicked = _pendingGridDisabledToggles.Contains((s.Row, s.Column));
            if (s.IsDisabled)
            {
                seats.Add(new SeatVisual(
                    s.SeatId, s.X, s.Y, s.Width, s.Height,
                    IsDisabled: true,
                    SeatLabel: string.Format(Resources.Venue_GridDisabledFmt, s.Row, s.Column),
                    Row: s.Row, Column: s.Column, IsSelected: isPicked));
                continue;
            }

            int deskNum = ((s.Column - 1) / Math.Max(1, meta.SeatsPerDesk)) + 1;
            seats.Add(new SeatVisual(
                s.SeatId, s.X, s.Y, s.Width, s.Height,
                IsOccupied: true,
                SeatLabel: string.Format(Resources.Venue_GridLabelFmt, s.Row, s.Column, deskNum),
                Row: s.Row, Column: s.Column, IsSelected: isPicked));
        }

        // 讲台（水平居中于网格）
        if (meta.HasPodium && meta.PodiumWidth > 0 && meta.PodiumHeight > 0 && seats.Count > 0)
        {
            double gridLeft = seats.Min(s => s.X);
            double gridRight = seats.Max(s => s.X + s.Width);
            double podiumW = meta.PodiumWidth * geometry.FactorX;
            double podiumX = ((gridLeft + gridRight) / 2) - (podiumW / 2);
            double podiumH = meta.PodiumHeight * geometry.FactorY;
            double podiumY = (meta.OriginY - meta.PodiumHeight - meta.VerticalSpacing) * geometry.FactorY;
            overlays.Add(new BoardOverlay(
                podiumX, podiumY, podiumW, podiumH, Resources.Freeform_Podium));
        }

        foreach (var door in DoorItems)
            overlays.Add(new BoardOverlay(
                door.X * geometry.FactorX, door.Y * geometry.FactorY, 36, 24, door.Label, IsDoor: true));
    }

    private void BuildPolarPreview(List<SeatVisual> seats, List<BoardOverlay> overlays)
    {
        var meta = BuildPolarMetadata(GetEffectivePolarEmptyPositions());
        var layout = PolarLayoutBuilder.BuildPolar(meta);
        int totalRings = meta.RingSeatCounts.Count > 0 ? meta.RingSeatCounts.Count : meta.Rings;

        const double seatR = 7;
        foreach (PolarSeat s in layout.Seats.Cast<PolarSeat>())
        {
            var (cx, cy) = SeatGeometryHelper.GetPosition(s, meta);
            seats.Add(new SeatVisual(
                s.Id, cx - seatR, cy - seatR, seatR * 2, seatR * 2,
                IsOccupied: true,
                SeatLabel: $"R{s.Ring} {s.AngleDegrees:F0}° ({s.LogicalGroup})",
                Ring: s.Ring, AngleDegrees: s.AngleDegrees,
                IsSelected: _pendingPolarDisabledToggles.Contains((s.Ring, Math.Round(s.AngleDegrees, 2)))));
        }

        // 讲台（圆心处）
        if (meta.HasPodium && meta.PodiumRadius > 0)
        {
            double pr = meta.PodiumRadius;
            overlays.Add(new BoardOverlay(
                meta.OriginX - pr, meta.OriginY - pr, pr * 2, pr * 2,
                Resources.Freeform_Podium, IsRound: true));
        }

        // 禁用座位标记
        if (meta.EmptyPositions is { Count: > 0 })
        {
            var circularAisleSet = new HashSet<int>(meta.AisleCircularAfterRings ?? []);
            foreach (var empty in meta.EmptyPositions)
            {
                int aislesBefore = circularAisleSet.Count(r => r < empty.Ring);
                double radius = (empty.Ring * meta.RadiusStep) + (aislesBefore * meta.AisleCircularWidth);
                double rad = empty.AngleDegrees * Math.PI / 180.0;
                double cx = meta.OriginX + (radius * Math.Cos(rad));
                double cy = meta.OriginY + (radius * Math.Sin(rad));
                seats.Add(new SeatVisual(
                    $"disabled-r{empty.Ring}a{empty.AngleDegrees:F2}", cx - seatR, cy - seatR, seatR * 2, seatR * 2,
                    IsDisabled: true,
                    SeatLabel: string.Format(Resources.Venue_PolarDisabledFmt, empty.Ring, empty.AngleDegrees),
                    Ring: empty.Ring, AngleDegrees: empty.AngleDegrees,
                    IsSelected: _pendingPolarDisabledToggles.Contains((empty.Ring, Math.Round(empty.AngleDegrees, 2)))));
            }
        }

        foreach (var door in DoorItems)
            overlays.Add(new BoardOverlay(door.X, door.Y, 36, 24, door.Label, IsDoor: true));
    }

    private void BuildFreeformPreview(List<SeatVisual> seats, List<BoardOverlay> overlays)
    {
        const double seatSize = 18;

        foreach (var p in Points)
        {
            switch (p.ElementType)
            {
                case (int)FreeformElementType.Seat:
                    seats.Add(new SeatVisual(
                        p.Id, p.X - (seatSize / 2), p.Y - (seatSize / 2), seatSize, seatSize,
                        IsOccupied: true,
                        SeatLabel: p.Row.HasValue && p.Column.HasValue
                            ? $"R{p.Row}C{p.Column}"
                            : $"({p.X:F0}, {p.Y:F0})"));
                    break;
                case (int)FreeformElementType.Podium:
                    double pw = p.Width > 0 ? p.Width : 60;
                    double ph = p.Height > 0 ? p.Height : 40;
                    overlays.Add(new BoardOverlay(p.X - (pw / 2), p.Y - (ph / 2), pw, ph,
                        Resources.Freeform_Podium, IsRound: true));
                    break;
                case (int)FreeformElementType.Door:
                    double dw = p.Width > 0 ? p.Width : 36;
                    double dh = p.Height > 0 ? p.Height : 24;
                    overlays.Add(new BoardOverlay(p.X - (dw / 2), p.Y - (dh / 2), dw, dh,
                        Resources.Freeform_Door, IsDoor: true));
                    break;
            }
        }
    }

    /// <summary>将内容平移至正坐标并计算板面尺寸（SeatingCanvas 会自动适配视口）。</summary>
    private static SeatLayoutSnapshot NormalizeSnapshot(List<SeatVisual> seats, List<BoardOverlay> overlays)
    {
        if (seats.Count == 0 && overlays.Count == 0)
            return SeatLayoutSnapshot.Empty;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        void Acc(double x, double y, double w, double h)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + w);
            maxY = Math.Max(maxY, y + h);
        }

        foreach (var s in seats)
            Acc(s.X, s.Y, s.Width, s.Height);
        foreach (var o in overlays)
            Acc(o.X, o.Y, o.Width, o.Height);

        const double padding = 24;
        double dx = padding - minX;
        double dy = padding - minY;

        var movedSeats = seats.Select(s => s with { X = s.X + dx, Y = s.Y + dy }).ToList();
        var movedOverlays = overlays.Select(o => o with { X = o.X + dx, Y = o.Y + dy }).ToList();

        return new SeatLayoutSnapshot(
            movedSeats,
            Math.Max(320, maxX + dx + padding),
            Math.Max(240, maxY + dy + padding),
            0,
            movedOverlays);
    }

    // ═══════════════════════════════════════════════
    // 构建 / 辅助
    // ═══════════════════════════════════════════════

    private ClassroomLayoutDefinition BuildLayoutDefinition()
    {
        ClassroomLayoutDefinition layout;
        switch (SelectedLayoutType)
        {
            case LayoutType.Grid:
                var meta = BuildGridMetadata();
                layout = GridLayoutBuilder.BuildGrid(meta);
                // 按位置匹配旧座位 ID，避免快照中 assignment 引用失效
                if (_existingGridSeatMap is { Count: > 0 } map)
                {
                    foreach (var s in layout.Seats.OfType<GridSeat>())
                    {
                        if (map.TryGetValue((s.Row, s.Column), out var oldId))
                            s.Id = oldId;
                    }
                }
                layout.Name = LayoutName;
                layout.Id = SelectedVenueItem?.Id ?? "";
                // 将讲台/门作为 Obstacle 写入（讲台居中于网格）
                if (meta.HasPodium && meta.PodiumWidth > 0 && meta.PodiumHeight > 0)
                {
                    double podiumW = meta.PodiumWidth;
                    double gridMidX = layout.Seats.Count > 0
                        ? (layout.Seats.Min(s => s is GridSeat g ? SeatGeometryHelper.GetPosition(s, meta).X : 0)
                         + layout.Seats.Max(s => s is GridSeat g ? SeatGeometryHelper.GetPosition(s, meta).X : 0)) / 2
                        : meta.OriginX;
                    layout.Obstacles.Add(new Obstacle
                    {
                        X = gridMidX - (podiumW / 2),
                        Y = meta.OriginY - meta.PodiumHeight - meta.VerticalSpacing,
                        Width = podiumW,
                        Height = meta.PodiumHeight,
                        Type = "Podium"
                    });
                }
                foreach (var door in DoorItems)
                {
                    layout.Obstacles.Add(new Obstacle
                    {
                        X = door.X,
                        Y = door.Y,
                        Width = 36,
                        Height = 24,
                        Type = "Door"
                    });
                }
                break;

            case LayoutType.Polar:
                var polarMeta = BuildPolarMetadata();
                layout = PolarLayoutBuilder.BuildPolar(polarMeta);
                if (_existingPolarSeatMap is { Count: > 0 } polarMap)
                {
                    foreach (var s in layout.Seats.OfType<PolarSeat>())
                    {
                        if (polarMap.TryGetValue((s.Ring, Math.Round(s.AngleDegrees, 2)), out var oldId))
                            s.Id = oldId;
                    }
                }
                layout.Name = LayoutName;
                layout.Id = SelectedVenueItem?.Id ?? "";
                foreach (var door in DoorItems)
                {
                    layout.Obstacles.Add(new Obstacle
                    {
                        X = door.X,
                        Y = door.Y,
                        Width = 36,
                        Height = 24,
                        Type = "Door"
                    });
                }
                break;

            case LayoutType.Freeform:
                var seatPoints = Points
                    .Where(p => p.ElementType == (int)FreeformElementType.Seat)
                    .Select(p => (p.X, p.Y, p.Row, p.Column, p.GroupId))
                    .ToList();
                var obstaclePoints = Points
                    .Where(p => p.ElementType is (int)FreeformElementType.Podium or (int)FreeformElementType.Door)
                    .Select(p => (p.X, p.Y,
                        p.Width > 0 ? p.Width : (p.ElementType == (int)FreeformElementType.Podium ? 60 : 36),
                        p.Height > 0 ? p.Height : (p.ElementType == (int)FreeformElementType.Podium ? 40 : 24),
                        p.ElementType == (int)FreeformElementType.Podium ? "Podium" : "Door"))
                    .ToList();

                layout = FreeformLayoutBuilder.BuildFreeform(
                    seatPoints,
                    obstaclePoints.Count > 0 ? obstaclePoints : null);

                // 自由点 ID 保留：按坐标表顺序复用点 Id（首次保存后的编辑不再重建快照引用）
                var builtSeats = layout.Seats.OfType<FreeformSeat>().ToList();
                var sourcePoints = Points.Where(p => p.ElementType == (int)FreeformElementType.Seat).ToList();
                for (int i = 0; i < builtSeats.Count && i < sourcePoints.Count; i++)
                    builtSeats[i].Id = sourcePoints[i].Id;

                layout.Id = SelectedVenueItem?.Id ?? "";
                layout.Name = LayoutName;
                break;

            default:
                layout = new ClassroomLayoutDefinition { Name = LayoutName };
                break;
        }

        return layout;
    }

    private GridLayoutMetadata BuildGridMetadata(List<GridPosition>? emptyPositionsOverride = null)
    {
        var columnRowCounts = ParseIntList(GridColumnRowCountsSpec);
        return new GridLayoutMetadata
        {
            Rows = GridRows,
            Columns = GridColumns,
            HorizontalSpacing = GridHorizontalSpacing,
            VerticalSpacing = GridVerticalSpacing,
            OriginX = GridOriginX,
            OriginY = GridOriginY,
            SeatsPerDesk = GridSeatsPerDesk,
            IntraDeskSpacing = GridIntraDeskSpacing,
            InterDeskSpacing = GridInterDeskSpacing,
            AisleAfterColumns = ParseIntList(GridAisleAfterColumns),
            AisleAfterRows = ParseIntList(GridAisleAfterRows),
            AisleWidth = GridAisleWidth,
            FrontRowCount = GridFrontRowCount,
            HasPodium = GridHasPodium,
            PodiumWidth = GridPodiumWidth,
            PodiumHeight = GridPodiumHeight,
            ColumnRowCounts = columnRowCounts,
            HasFrontDoor = GridHasFrontDoor,
            EmptyPositions = emptyPositionsOverride
                ?? FilterGridEmptyPositions(
                    ParseGridEmptyPositions(GridEmptyPositionsSpec), GridColumns, GridRows, columnRowCounts)
        };
    }

    private PolarLayoutMetadata BuildPolarMetadata(List<PolarRingAngle>? emptyPositionsOverride = null)
    {
        var ringSeatCounts = ParseIntList(PolarRingSeatCountsSpec);
        return new PolarLayoutMetadata
        {
            Rings = PolarRings,
            SeatsPerRing = PolarSeatsPerRing,
            RadiusStep = PolarRadiusStep,
            StartAngleDegrees = PolarStartAngle,
            EndAngleDegrees = PolarEndAngle,
            OriginX = PolarOriginX,
            OriginY = PolarOriginY,
            RingSeatCounts = ringSeatCounts,
            HasPodium = PolarHasPodium,
            PodiumRadius = PolarPodiumRadius,
            AisleRadialAngles = ParseDoubleList(PolarAisleRadialAngles),
            AisleRadialWidthDegrees = PolarAisleRadialWidth,
            AisleCircularAfterRings = ParseIntList(PolarAisleCircularRings),
            AisleCircularWidth = PolarAisleCircularWidth,
            FrontRowCount = PolarFrontRowCount,
            EmptyPositions = emptyPositionsOverride
                ?? FilterPolarEmptyPositions(
                    ParsePolarEmptyPositions(PolarEmptyPositionsSpec), ringSeatCounts, PolarRings)
        };
    }

    private void RegenerateAisleOptions()
    {
        var prevCols = new HashSet<int>(ParseIntList(GridAisleAfterColumns));
        var prevRows = new HashSet<int>(ParseIntList(GridAisleAfterRows));
        int spd = GridSeatsPerDesk > 0 ? GridSeatsPerDesk : 1;

        // 列过道选项：以桌列为单位，标签取「第 N 列后」，tooltip 保留完整区间语义
        int deskCols = GridColumns / spd;
        var colTargets = new List<(string Label, int SeatColumn, string ToolTip)>();
        for (int d = 1; d < deskCols; d++)
        {
            int seatCol = d * spd; // 过道在该座位列索引之后
            colTargets.Add((
                string.Format(Resources.Venue_ColAisleFmt, seatCol),
                seatCol,
                string.Format(Resources.Venue_ColAisleTooltipFmt, seatCol, seatCol + 1)));
        }
        SyncAisleOptions(AisleColumnOptions, colTargets, prevCols, SyncAisleColumnsFromOptions);

        // 行过道选项
        var rowTargets = new List<(string Label, int SeatColumn, string ToolTip)>();
        for (int r = 1; r < GridRows; r++)
        {
            rowTargets.Add((
                string.Format(Resources.Venue_RowAisleFmt, r),
                r,
                string.Format(Resources.Venue_RowAisleTooltipFmt, r, r + 1)));
        }
        SyncAisleOptions(AisleRowOptions, rowTargets, prevRows, SyncAisleRowsFromOptions);
    }

    /// <summary>
    /// 按当前列数增量同步「每列行数」输入项：新增列从 spec 回填/留空，超出列截断；
    /// 末尾统一回写 spec（内部空位用当前行数补齐、末尾空位省略）。
    /// </summary>
    private void RegenerateColumnRowCountOptions()
    {
        int target = Math.Max(1, GridColumns);
        var spec = ParseIntList(GridColumnRowCountsSpec);

        while (ColumnRowCountOptions.Count > target)
            ColumnRowCountOptions.RemoveAt(ColumnRowCountOptions.Count - 1);

        while (ColumnRowCountOptions.Count < target)
        {
            int index = ColumnRowCountOptions.Count;
            var option = new ColumnRowCountOption(index + 1)
            {
                Rows = index < spec.Count ? spec[index] : null
            };
            option.PropertyChanged += (_, _) => SyncColumnRowCountsFromOptions();
            ColumnRowCountOptions.Add(option);
        }

        SyncColumnRowCountsFromOptions();
    }

    private void SyncColumnRowCountsFromOptions()
    {
        GridColumnRowCountsSpec = BuildColumnRowCountsSpec();
    }

    /// <summary>
    /// 生成每列行数 spec：取最后一个有值列，其前空位用当前行数补齐，末尾空位省略；
    /// 全空返回空串（全部沿用「行数」）。
    /// </summary>
    private string BuildColumnRowCountsSpec()
    {
        int last = -1;
        for (int i = 0; i < ColumnRowCountOptions.Count; i++)
        {
            if (ColumnRowCountOptions[i].Rows is > 0) last = i;
        }

        if (last < 0) return "";

        var parts = new string[last + 1];
        for (int i = 0; i <= last; i++)
        {
            var rows = ColumnRowCountOptions[i].Rows;
            parts[i] = (rows is > 0 ? rows.Value : Math.Max(1, GridRows)).ToString(CultureInfo.InvariantCulture);
        }

        return string.Join(",", parts);
    }

    /// <summary>
    /// 增量同步过道选项集合：仅追加/移除差异项，避免整体替换 ObservableCollection
    /// 触发 ItemsControl 全量容器重建（M2 性能验收的关键修复）。
    /// </summary>
    private static void SyncAisleOptions(
        ObservableCollection<AisleOption> current,
        List<(string Label, int SeatColumn, string ToolTip)> targets,
        HashSet<int> previousSelection,
        Action onSelectionChanged)
    {
        bool structureChanged = false;

        while (current.Count > targets.Count)
        {
            current.RemoveAt(current.Count - 1);
            structureChanged = true;
        }

        for (int i = 0; i < current.Count; i++)
        {
            var (label, seatColumn, toolTip) = targets[i];
            var option = current[i];

            if (option.SeatColumn != seatColumn)
            {
                // 结构变化（如改变每桌人数）：保留原勾选状态迁移到新列位
                var replacement = new AisleOption(label, seatColumn, toolTip, option.IsSelected);
                replacement.PropertyChanged += (_, _) => onSelectionChanged();
                current[i] = replacement;
                structureChanged = true;
            }
            else
            {
                if (option.Label != label) option.Label = label;
                if (option.ToolTip != toolTip) option.ToolTip = toolTip;
            }
        }

        for (int i = current.Count; i < targets.Count; i++)
        {
            var (label, seatColumn, toolTip) = targets[i];
            var option = new AisleOption(label, seatColumn, toolTip, previousSelection.Contains(seatColumn));
            option.PropertyChanged += (_, _) => onSelectionChanged();
            current.Add(option);
        }

        // 结构变化后必须把迁移/移除后的勾选状态回写 spec，否则会保留已失效的过道位置
        if (structureChanged)
            onSelectionChanged();
    }

    /// <summary>过道勾选状态变化时同步回字符串。</summary>
    private void SyncAisleColumnsFromOptions()
    {
        var selected = AisleColumnOptions.Where(o => o.IsSelected).Select(o => o.SeatColumn);
        GridAisleAfterColumns = string.Join(",", selected);
    }

    private void SyncAisleRowsFromOptions()
    {
        var selected = AisleRowOptions.Where(o => o.IsSelected).Select(o => o.SeatColumn);
        GridAisleAfterRows = string.Join(",", selected);
    }

    private void RestoreObstaclesFromLayout(ClassroomLayoutDefinition layout)
    {
        var doors = layout.Obstacles.Where(o => o.Type == "Door").ToList();
        DoorItems.Clear();
        foreach (var (d, i) in doors.Select((d, i) => (d, i)))
            DoorItems.Add(new DoorItem(d.X, d.Y, string.Format(Resources.Venue_DoorFmt, i + 1)));

        if (layout.Metadata is GridLayoutMetadata gridMeta)
            GridHasFrontDoor = gridMeta.HasFrontDoor;
    }

    private void PopulateGridFromMetadata(GridLayoutMetadata g)
    {
        GridRows = g.Rows > 0 ? g.Rows : 5;
        GridColumns = g.Columns > 0 ? g.Columns : 8;
        GridHorizontalSpacing = g.HorizontalSpacing > 0 ? g.HorizontalSpacing : 40;
        GridVerticalSpacing = g.VerticalSpacing > 0 ? g.VerticalSpacing : 36;
        GridOriginX = g.OriginX > 0 ? g.OriginX : 200;
        GridOriginY = g.OriginY > 0 ? g.OriginY : 200;
        GridSeatsPerDesk = g.SeatsPerDesk > 0 ? g.SeatsPerDesk : 2;
        GridIntraDeskSpacing = g.IntraDeskSpacing > 0 ? g.IntraDeskSpacing : 12;
        GridInterDeskSpacing = g.InterDeskSpacing > 0 ? g.InterDeskSpacing : 32;
        GridAisleAfterColumns = string.Join(",", g.AisleAfterColumns ?? []);
        GridAisleAfterRows = string.Join(",", g.AisleAfterRows ?? []);
        GridAisleWidth = g.AisleWidth > 0 ? g.AisleWidth : 60;
        GridFrontRowCount = g.FrontRowCount > 0 ? g.FrontRowCount : 1;
        GridHasPodium = g.HasPodium;
        GridPodiumWidth = g.PodiumWidth > 0 ? g.PodiumWidth : 60;
        GridPodiumHeight = g.PodiumHeight > 0 ? g.PodiumHeight : 40;
        GridColumnRowCountsSpec = g.ColumnRowCounts is { Count: > 0 } ? string.Join(",", g.ColumnRowCounts) : "";
        GridEmptyPositionsSpec = g.EmptyPositions is { Count: > 0 }
            ? string.Join(";", g.EmptyPositions.Select(p => $"{p.Row},{p.Column}"))
            : "";

        RegenerateAisleOptions();
        // 选项值必须完全来自本次加载的 spec，避免沿用上一个会场的「每列行数」
        ColumnRowCountOptions.Clear();
        RegenerateColumnRowCountOptions();
    }

    private void PopulatePolarFromMetadata(PolarLayoutMetadata p)
    {
        PolarRings = p.Rings > 0 ? p.Rings : 3;
        PolarSeatsPerRing = p.SeatsPerRing > 0 ? p.SeatsPerRing : 12;
        PolarRadiusStep = p.RadiusStep > 0 ? p.RadiusStep : 40;
        PolarStartAngle = p.StartAngleDegrees;
        PolarEndAngle = p.EndAngleDegrees > 0 ? p.EndAngleDegrees : 360;
        PolarOriginX = p.OriginX > 0 ? p.OriginX : 200;
        PolarOriginY = p.OriginY > 0 ? p.OriginY : 200;
        PolarRingSeatCountsSpec = p.RingSeatCounts is { Count: > 0 } ? string.Join(",", p.RingSeatCounts) : "";
        PolarHasPodium = p.HasPodium;
        PolarPodiumRadius = p.PodiumRadius > 0 ? p.PodiumRadius : 30;
        PolarAisleRadialAngles = p.AisleRadialAngles is { Count: > 0 } ? string.Join(",", p.AisleRadialAngles.Select(a => a.ToString("F1"))) : "";
        PolarAisleRadialWidth = p.AisleRadialWidthDegrees > 0 ? p.AisleRadialWidthDegrees : 5;
        PolarAisleCircularRings = p.AisleCircularAfterRings is { Count: > 0 } ? string.Join(",", p.AisleCircularAfterRings) : "";
        PolarAisleCircularWidth = p.AisleCircularWidth > 0 ? p.AisleCircularWidth : 20;
        PolarFrontRowCount = p.FrontRowCount > 0 ? p.FrontRowCount : 1;
        PolarEmptyPositionsSpec = p.EmptyPositions is { Count: > 0 }
            ? string.Join(";", p.EmptyPositions.Select(e => $"{e.Ring},{e.AngleDegrees:F2}"))
            : "";
    }

    private void PopulateFreeformFromLayout(ClassroomLayoutDefinition layout)
    {
        Points.Clear();
        foreach (var s in layout.Seats.OfType<FreeformSeat>())
        {
            int? groupId = null;
            if (!string.IsNullOrEmpty(s.LogicalGroup) && s.LogicalGroup.StartsWith('G')
                && int.TryParse(s.LogicalGroup[1..], out var gid))
                groupId = gid;
            Points.Add(new FreeformPoint(s.X, s.Y, s.Id)
            {
                ElementType = (int)FreeformElementType.Seat,
                GroupId = groupId,
                Row = s.Row,
                Column = s.Column
            });
        }
        foreach (var obs in layout.Obstacles)
        {
            var et = obs.Type == "Podium" ? (int)FreeformElementType.Podium
                   : obs.Type == "Door" ? (int)FreeformElementType.Door
                   : (int)FreeformElementType.Seat;
            Points.Add(new FreeformPoint(obs.X, obs.Y)
            {
                ElementType = et,
                Width = obs.Width,
                Height = obs.Height
            });
        }
        RefreshIndices();
    }

    private void ResetParameters()
    {
        GridRows = 5; GridColumns = 8;
        GridHorizontalSpacing = 64; GridVerticalSpacing = 56;
        GridOriginX = 200; GridOriginY = 200;
        GridSeatsPerDesk = 2;
        GridIntraDeskSpacing = 40; GridInterDeskSpacing = 32;
        GridAisleAfterColumns = ""; GridAisleAfterRows = "";
        GridAisleWidth = 60;
        GridFrontRowCount = 1;
        GridHasPodium = true; GridPodiumWidth = 100; GridPodiumHeight = 40;
        GridColumnRowCountsSpec = ""; GridEmptyPositionsSpec = "";
        ColumnRowCountOptions.Clear();
        DoorItems.Clear();
        PolarRings = 3; PolarSeatsPerRing = 12;
        PolarRadiusStep = 40; PolarStartAngle = 0; PolarEndAngle = 360;
        PolarOriginX = 200; PolarOriginY = 200;
        PolarRingSeatCountsSpec = "";
        PolarHasPodium = true; PolarPodiumRadius = 30;
        PolarAisleRadialAngles = ""; PolarAisleRadialWidth = 5;
        PolarAisleCircularRings = ""; PolarAisleCircularWidth = 20;
        PolarFrontRowCount = 1;
        PolarEmptyPositionsSpec = "";
        // 规格清零后重建过道选项，避免勾选状态与规格不一致
        RegenerateAisleOptions();
        RegenerateColumnRowCountOptions();
        ResetDisabledSeatsPicking();
        Points.Clear();
        RefreshPointsState();
    }

    private static List<int> ParseIntList(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return [.. csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var n) ? n : -1)
            .Where(n => n > 0)];
    }

    private static List<double> ParseDoubleList(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return [.. csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.TryParse(s.Trim(), out var n) ? n : -1)
            .Where(n => n >= 0)];
    }

    private static List<GridPosition> ParseGridEmptyPositions(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return [];
        return [.. spec.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var parts = part.Split(',');
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), out var row)
                    && int.TryParse(parts[1].Trim(), out var col)
                    && row > 0 && col > 0)
                    return new GridPosition { Row = row, Column = col };
                return null;
            })
            .Where(p => p != null)
            .Cast<GridPosition>()];
    }

    private static List<PolarRingAngle> ParsePolarEmptyPositions(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return [];
        return [.. spec.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var parts = part.Split(',');
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), out var ring)
                    && double.TryParse(parts[1].Trim(), out var angle)
                    && ring > 0)
                    return new PolarRingAngle { Ring = ring, AngleDegrees = angle };
                return null;
            })
            .Where(p => p != null)
            .Cast<PolarRingAngle>()];
    }

    /// <summary>过滤掉行列号超出有效范围的禁用位置。</summary>
    private static List<GridPosition> FilterGridEmptyPositions(List<GridPosition> raw, int columns, int defaultRows, List<int> columnRowCounts)
    {
        if (raw.Count == 0) return raw;
        return [.. raw.Where(p =>
        {
            int maxRows = columnRowCounts is { Count: > 0 } && p.Column <= columnRowCounts.Count
                ? columnRowCounts[p.Column - 1] : defaultRows;
            return p.Row >= 1 && p.Column >= 1 && p.Row <= maxRows && p.Column <= columns;
        })];
    }

    /// <summary>过滤掉环号超出有效范围的禁用位置。</summary>
    private static List<PolarRingAngle> FilterPolarEmptyPositions(List<PolarRingAngle> raw, List<int> ringSeatCounts, int defaultRings)
    {
        if (raw.Count == 0) return raw;
        int totalRings = ringSeatCounts.Count > 0 ? ringSeatCounts.Count : defaultRings;
        return [.. raw.Where(p => p.Ring >= 1 && p.Ring <= totalRings)];
    }

    // ═══════════════════════════════════════════════
    // 状态跟踪 / 脏检查 / 集合订阅
    // ═══════════════════════════════════════════════

    partial void OnSelectedVenueItemChanged(VenueItem? value)
    {
        if (!_suppressAutoLoad && value != null)
        {
            _selectVenueCts?.Cancel();
            _selectVenueCts = new CancellationTokenSource();
            _ = SelectVenueAsync(value, _selectVenueCts.Token);
        }
    }

    /// <summary>
    /// 单一属性变更入口：命名属性 → 脏标记 + 预览去抖（替代 40 个 OnXxxChanged）。
    /// </summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        var name = e.PropertyName;
        if (name is null) return;

        if (name is nameof(GridEmptyPositionsSpec) or nameof(PolarEmptyPositionsSpec) or nameof(SelectedLayoutType))
            OnPropertyChanged(nameof(CanClearDisabledSeats));

        if (name == nameof(LayoutName))
        {
            MarkEditorChanged(recompute: false);
            return;
        }

        if (EditorPropertyNames.Contains(name))
        {
            // 行/列/每桌座位数变化时的过道选项重建已移入去抖后的 RecomputePreviewNow（避免逐键重建控件）
            MarkEditorChanged(recompute: true);
        }
    }

    private void MarkEditorChanged(bool recompute)
    {
        if (_suppressEditorTracking) return;

        DirtyTracker.Update(BuildDirtySnapshot());

        if (recompute)
        {
            IsRecomputing = true;
            PreviewRevision++;
        }
    }

    /// <summary>轻量编辑器状态快照：标量参数 + 点/门修订号（避免逐点序列化开销）。</summary>
    private string BuildDirtySnapshot()
    {
        var sb = new StringBuilder(256);
        sb.Append(LayoutName).Append('|').Append((int)SelectedLayoutType).Append('|')
          .Append(GridRows).Append(',').Append(GridColumns).Append(',')
          .Append(GridHorizontalSpacing).Append(',').Append(GridVerticalSpacing).Append(',')
          .Append(GridOriginX).Append(',').Append(GridOriginY).Append(',')
          .Append(GridSeatsPerDesk).Append(',').Append(GridIntraDeskSpacing).Append(',').Append(GridInterDeskSpacing).Append(',')
          .Append(GridAisleAfterColumns).Append(',').Append(GridAisleAfterRows).Append(',').Append(GridAisleWidth).Append(',')
          .Append(GridFrontRowCount).Append(',').Append(GridHasPodium).Append(',').Append(GridPodiumWidth).Append(',').Append(GridPodiumHeight).Append(',')
          .Append(GridColumnRowCountsSpec).Append(',').Append(GridEmptyPositionsSpec).Append(',')
          .Append(PolarRings).Append(',').Append(PolarSeatsPerRing).Append(',').Append(PolarRadiusStep).Append(',')
          .Append(PolarStartAngle).Append(',').Append(PolarEndAngle).Append(',')
          .Append(PolarOriginX).Append(',').Append(PolarOriginY).Append(',')
          .Append(PolarRingSeatCountsSpec).Append(',').Append(PolarEmptyPositionsSpec).Append(',')
          .Append(PolarHasPodium).Append(',').Append(PolarPodiumRadius).Append(',')
          .Append(PolarAisleRadialAngles).Append(',').Append(PolarAisleRadialWidth).Append(',')
          .Append(PolarAisleCircularRings).Append(',').Append(PolarAisleCircularWidth).Append(',')
          .Append(PolarFrontRowCount)
          .Append('|').Append(_freeformPointsRevision).Append(':').Append(Points.Count)
          .Append('|').Append(_doorRevision).Append(':').Append(DoorItems.Count);
        return sb.ToString();
    }

    private void SubscribeToDoorCollection(ObservableCollection<DoorItem> doors)
    {
        doors.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (DoorItem item in e.NewItems)
                    item.PropertyChanged += OnDoorItemPropertyChanged;
            }
            if (e.OldItems != null)
            {
                foreach (DoorItem item in e.OldItems)
                    item.PropertyChanged -= OnDoorItemPropertyChanged;
            }
            _doorRevision++;
            MarkEditorChanged(recompute: true);
        };
        foreach (var door in doors)
            door.PropertyChanged += OnDoorItemPropertyChanged;
    }

    private void OnDoorItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DoorItem.X) or nameof(DoorItem.Y))
        {
            _doorRevision++;
            MarkEditorChanged(recompute: true);
        }
    }

    private void SubscribeToPointsCollection()
    {
        Points.CollectionChanged += OnPointsCollectionChanged;
    }

    private void OnPointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (FreeformPoint p in e.NewItems)
                p.PropertyChanged += OnFreeformPointPropertyChanged;
        }
        if (e.OldItems != null)
        {
            foreach (FreeformPoint p in e.OldItems)
                p.PropertyChanged -= OnFreeformPointPropertyChanged;
        }

        _freeformPointsRevision++;
        RefreshPointsState();
        MarkEditorChanged(recompute: SelectedLayoutType == LayoutType.Freeform);
    }

    private void OnFreeformPointPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _freeformPointsRevision++;
        MarkEditorChanged(recompute: SelectedLayoutType == LayoutType.Freeform);
    }

    private void RefreshPointsState()
    {
        OnPropertyChanged(nameof(HasPoints));
        OnPropertyChanged(nameof(ElementCountDisplay));
        OnPropertyChanged(nameof(IsFreeformEmptyState));
        OnPropertyChanged(nameof(CanSaveVenue));
    }

    // ═══════════════════════════════════════════════
    // 离开确认
    // ═══════════════════════════════════════════════

    public override async Task<bool> CanLeaveAsync()
    {
        // 拾取草稿先写入编辑器状态（未保存的禁用选择不因离开而丢失）
        CommitPendingDisabledToggles();

        // 兜底刷新（覆盖个别不触发通知的编辑路径，如虚拟化单元格）
        DirtyTracker.Update(BuildDirtySnapshot());

        if (!DirtyTracker.IsDirty || SelectedVenueItem is null)
        {
            ClearVenueState();
            return true;
        }

        var choice = await Dialog.ShowMultiOptionAsync(
            Resources.Venue_UnsavedChanges,
            Resources.Venue_UnsavedChangesMsg,
            Resources.Common_Save,
            Resources.Common_Discard,
            Resources.Common_Cancel);

        switch (choice)
        {
            case 0: // 保存
                await SaveVenue();
                break;
            case 1: // 放弃
                break;
            default: // 取消
                return false;
        }

        ClearVenueState();
        return true;
    }

    private void ClearVenueState()
    {
        _selectVenueCts?.Cancel();
        ResetDisabledSeatsPicking();
        _suppressAutoLoad = true;
        _suppressEditorTracking = true;
        try
        {
            SelectedVenueItem = null;
            LayoutName = string.Empty;
            _existingGridSeatMap = null;
            _existingPolarSeatMap = null;
            DoorItems.Clear();
            ColumnRowCountOptions.Clear();
            Points.Clear();
            PreviewSnapshot = null;
            PreviewSeatCount = 0;
            // 以清空后的状态作为干净基线（后续编辑仍能正确判定脏）
            DirtyTracker.MarkClean(BuildDirtySnapshot());
        }
        finally
        {
            _suppressEditorTracking = false;
            _suppressAutoLoad = false;
        }
        RefreshPointsState();
    }
}

public record VenueItem(string Id, string Name);

public partial class DoorItem : ObservableObject
{
    [ObservableProperty]
    public partial double X { get; set; }

    [ObservableProperty]
    public partial double Y { get; set; }

    [ObservableProperty]
    public partial string Label { get; set; } = Resources.Freeform_Door;

    public DoorItem() { }
    public DoorItem(double x, double y, string? label = null)
    {
        X = x;
        Y = y;
        Label = label ?? Resources.Freeform_Door;
    }
}

public partial class AisleOption(string label, int seatColumn, string toolTip, bool selected = false) : ObservableObject
{
    public string Label { get; set; } = label;
    public int SeatColumn { get; set; } = seatColumn;
    public string ToolTip { get; set; } = toolTip;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = selected;
}

/// <summary>「每列行数」输入项：列号固定，行数留空 = 沿用全局「行数」。</summary>
public partial class ColumnRowCountOption(int column) : ObservableObject
{
    public int Column { get; } = column;

    public string Label => string.Format(Resources.Venue_ColumnRowsFmt, Column);

    [ObservableProperty]
    public partial int? Rows { get; set; }
}

/// <summary>自由点元素类型（与旧自由点页一致：0=座位, 1=讲台, 2=门）。</summary>
public enum FreeformElementType
{
    Seat,
    Podium,
    Door
}

/// <summary>自由点坐标（可观察，供表格双向编辑并驱动预览去抖重算）。</summary>
public partial class FreeformPoint : ObservableObject
{
    /// <summary>座位/障碍物稳定 ID（保存时复用，避免快照 assignment 引用失效）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TooltipDisplay))]
    public partial int ElementType { get; set; }

    [ObservableProperty]
    public partial double X { get; set; }

    [ObservableProperty]
    public partial double Y { get; set; }

    [ObservableProperty]
    public partial int? GroupId { get; set; }

    [ObservableProperty]
    public partial int? Row { get; set; }

    [ObservableProperty]
    public partial int? Column { get; set; }

    [ObservableProperty]
    public partial double Width { get; set; }

    [ObservableProperty]
    public partial double Height { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TooltipDisplay))]
    public partial int DisplayIndex { get; set; }

    public string GroupColor { get; set; } = GetGroupColor(null);

    public string TooltipDisplay => ElementType switch
    {
        (int)FreeformElementType.Seat => string.Format(Resources.Freeform_SeatFmt, DisplayIndex),
        (int)FreeformElementType.Podium => string.Format(Resources.Freeform_PodiumFmt, DisplayIndex),
        (int)FreeformElementType.Door => string.Format(Resources.Freeform_DoorFmt, DisplayIndex),
        _ => $"#{DisplayIndex}"
    };

    private static readonly string[] GroupColors =
        ["#4A90D9", "#E74C3C", "#2ECC71", "#F39C12", "#9B59B6", "#1ABC9C", "#E67E22", "#3498DB"];

    public static string GetGroupColor(int? groupId)
        => groupId is >= 0 and < 8 ? GroupColors[groupId.Value] : "#4A90D9";

    public FreeformPoint() { }

    public FreeformPoint(double x, double y, string? id = null)
    {
        X = x;
        Y = y;
        Id = id ?? Guid.NewGuid().ToString();
        GroupColor = GetGroupColor(null);
    }
}
