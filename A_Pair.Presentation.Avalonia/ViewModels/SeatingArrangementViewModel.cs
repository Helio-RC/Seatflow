using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.DomainServices;
using A_Pair.Core.Models;
using A_Pair.Core.Workspace;
using A_Pair.Presentation.Avalonia.Lang;
using A_Pair.Presentation.Avalonia.Services;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class SeatingArrangementViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly IFileService _fileService;
    private readonly ILogger<SeatingArrangementViewModel> _logger;

    // ── 内部状态 ──
    private SeatingWorkspace? _workspace;
    private ClassroomLayoutDefinition? _currentLayout;
    private SeatingPlan? _currentPlan;
    private SeatDisplayItem? _swapSourceSeat;
    private CancellationTokenSource? _generateCts;

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
    private ObservableCollection<VenueItem> _venueItems = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVenue))]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    private VenueItem? _selectedVenue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDataset))]
    private ObservableCollection<StudentDatasetInfo> _datasetItems = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDataset))]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    private StudentDatasetInfo? _selectedDataset;

    public bool HasSelectedVenue => SelectedVenue != null;
    public bool HasSelectedDataset => SelectedDataset != null;

    // ── Canvas ──
    [ObservableProperty]
    private ObservableCollection<SeatDisplayItem> _seatItems = [];

    [ObservableProperty]
    private ObservableCollection<SeatDisplayItem> _overlayItems = [];

    [ObservableProperty]
    private double _canvasWidth = 800;

    [ObservableProperty]
    private double _canvasHeight = 600;

    private double _contentCenterX, _contentCenterY;
    private double _defaultZoomLevel = 1.0;
    public double ZoomLevel { get; set; } = 1.0;
    public Action<double> ZoomAction => delta => ApplyZoom(delta);
    public void ApplyZoom (double delta) { ZoomLevel = Math.Clamp(ZoomLevel + delta , 0.2 , 3.0); BuildSeatDisplayItems(); }

    /// <summary>不改变数据，仅重新绘制预览区域。</summary>
    public void RefreshPreview () => BuildSeatDisplayItems();

    // ── 工具栏 ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    private bool _isGenerating;

    [ObservableProperty]
    private bool _hasGenerated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    [NotifyPropertyChangedFor(nameof(CanRedo))]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private bool _hasHistoryUnsaved;

    public bool CanGenerate => HasSelectedVenue && HasSelectedDataset && !IsGenerating;
    public bool CanUndo => _currentHistoryIndex > 0;
    public bool CanRedo => _currentHistoryIndex < _historyEntries.Count - 1;

    // ── 右侧面板 ──
    [ObservableProperty]
    private ObservableCollection<StrategyDisplayInfo> _activeStrategies = [];

    [ObservableProperty]
    private ObservableCollection<Student> _unassignedStudents = [];

    [ObservableProperty]
    private bool _isStrategiesExpanded = true;

    [ObservableProperty]
    private bool _isUnassignedExpanded = true;

    [ObservableProperty]
    private bool _isHistoryExpanded = true;

    // ── 状态栏 ──
    [ObservableProperty]
    private string _statusMessage = Resources.Seating_Ready;

    [ObservableProperty]
    private int _totalSeats;

    [ObservableProperty]
    private int _assignedSeats;

    public int UnassignedStudentCount => UnassignedStudents.Count;

    // ── 交换模式 ──
    [ObservableProperty]
    private bool _isSwapMode;

    [ObservableProperty]
    private string _swapHintText = string.Empty;

    public SeatingArrangementViewModel (IApplicationFacade facade , IFileService fileService , INavigationService navigation , ILogger<SeatingArrangementViewModel>? logger = null)
    {
        _facade = facade;
        _fileService = fileService;
        _navigation = navigation;
        _logger = logger ?? NullLogger<SeatingArrangementViewModel>.Instance;
        navigation.CurrentViewModelChanged += OnNavigationChanged;
        _ = LoadInitialDataAsync();
    }

    private readonly INavigationService _navigation;

    private void OnNavigationChanged ()
    {
        if (_navigation.CurrentViewModel == this)
        {
            // 推迟到 UI 布局完成后执行，确保 OnLoaded 已触发、SeatItems 绑定已建立
            Dispatcher.UIThread.Post(() => _ = RefreshDataAsync());
        }
    }

    /// <summary>用于抑制 <see cref="OnSelectedVenueChanged"/> 覆盖已恢复的布局。</summary>
    private bool _isRestoringWorkspace;

    // ── 初始化 ──

    private async Task LoadInitialDataAsync ()
    {
        await Task.WhenAll(LoadVenuesAsync() , LoadDatasetsAsync() , LoadDefaultZoomAsync());
        StatusMessage = Resources.Seating_ReadyHint;
    }

    private async Task LoadDefaultZoomAsync ()
    {
        try
        {
            var settings = await _facade.LoadAppSettingsAsync();
            _defaultZoomLevel = settings.DefaultZoomLevel;
            ZoomLevel = _defaultZoomLevel;
        }
        catch { /* 读取失败使用默认 1.0 */ }
    }

    public async Task RefreshDataAsync ()
    {
        await Task.WhenAll(LoadVenuesAsync() , LoadDatasetsAsync());
        await TryRestoreWorkspaceAsync();
    }

    [RelayCommand]
    private async Task LoadVenuesAsync ()
    {
        await SafeExecuteAsync(async () =>
        {
            var ids = (await _facade.ListVenueIdsAsync()).ToList();
            var items = new List<VenueItem>();
            foreach (var id in ids)
            {
                var layout = await _facade.LoadVenueAsync(id);
                items.Add(new VenueItem(id , layout?.Name ?? id));
            }
            VenueItems = new ObservableCollection<VenueItem>(items);
        });
    }

    [RelayCommand]
    private async Task LoadDatasetsAsync ()
    {
        await SafeExecuteAsync(async () =>
        {
            var datasets = await _facade.ListStudentDatasetsAsync();
            DatasetItems = new ObservableCollection<StudentDatasetInfo>(datasets);
        });
    }

    /// <summary>如果有活跃工作区（如快照回滚后），恢复座位图显示。</summary>
    private async Task TryRestoreWorkspaceAsync ()
    {
        _workspace = await _facade.GetCurrentWorkspaceAsync();
        if (_workspace == null) return;

        _currentLayout = await _facade.GetCurrentLayoutAsync();
        _currentPlan = _workspace.BuildSeatingPlan();

        if (_currentLayout != null)
        {
            _isRestoringWorkspace = true;
            SelectedVenue = VenueItems.FirstOrDefault(v => v.Id == _currentLayout.Id)
                         ?? VenueItems.FirstOrDefault();
            _isRestoringWorkspace = false;

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
    partial void OnSelectedVenueChanged (VenueItem? value)
    {
        if (value == null || _isRestoringWorkspace) return;
        _ = SafeExecuteAsync(async () =>
        {
            _currentLayout = await _facade.LoadVenueAsync(value.Id);
            if (_currentLayout != null)
            {
                ObstacleProcessor.ApplyObstacles(_currentLayout);
                StatusMessage = string.Format(Resources.Seating_VenueLoadedFmt, _currentLayout.Name, _currentLayout.Seats.Count);
            }
        });
    }

    // ── 生成座位 ──

    [RelayCommand]
    private async Task GenerateSeatingAsync ()
    {
        if (!CanGenerate || _currentLayout == null) return;

        _generateCts?.Cancel();
        _generateCts = new CancellationTokenSource();
        var ct = _generateCts.Token;

        IsGenerating = true;
        HasGenerated = false;
        StatusMessage = Resources.Seating_Generating;

        await SafeExecuteAsync(async () =>
        {
            // 1. 加载学生
            var students = await _facade.LoadStudentDatasetAsync(SelectedDataset!.Id , ct);
            if (students == null || students.Count == 0)
            {
                StatusMessage = Resources.Seating_NoStudents;
                return;
            }

            // 2. 写入临时 JSON 文件（RosterFile 格式）
            var roster = new RosterFile { Version = "1.0" , Students = students };
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true ,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var tempPath = Path.Combine(Path.GetTempPath() , $"a_pair_gen_{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(tempPath , JsonSerializer.Serialize(roster , jsonOptions) , ct);

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
                    LayoutId = SelectedVenue!.Id ,
                    StudentDataSource = tempPath ,
                    Description = string.Format(Resources.Seating_VenueDatasetDesc, SelectedVenue.Name, SelectedDataset.Name)
                };

                _workspace = await _facade.GenerateSeatingAsync(request , progress , ct);
                _currentPlan = _workspace.BuildSeatingPlan();

                // 4. 构建显示 + 初始化历史
                BuildSeatDisplayItems();
                await UpdateRightPanelAsync();
                UpdateStats();
                InitHistory(Resources.Seating_GenerateDesc);

                HasGenerated = true;
                StatusMessage = string.Format(Resources.Seating_GeneratedFmt, AssignedSeats, TotalSeats);
            }
            finally
            {
                // 5. 清理临时文件
                try { File.Delete(tempPath); } catch { /* 忽略 */ }
            }
        } , Resources.Seating_GenerateFailed);

        IsGenerating = false;
    }

    // ── Canvas 数据构建 ──

    private void BuildSeatDisplayItems ()
    {
        if (_currentLayout == null || _workspace == null || _currentPlan == null) return;

        var metadata = _currentLayout.Metadata;
        var studentMap = _workspace.Students.ToDictionary(s => s.Id , s => s.Name);
        var assignments = _currentPlan.Assignments;
        var (baseW , baseH) = GetSeatDimensions(metadata);

        // 网格布局扩距系数（增大间距防重叠）
        double spread = metadata is GridLayoutMetadata ? 1.8 : 1.0;

        // 第一遍：收集原始坐标范围
        double minX0 = double.MaxValue, minY0 = double.MaxValue;
        double maxX0 = 0, maxY0 = 0;
        var rawPositions = new List<(double cx , double cy , Seat seat)>();
        foreach (var seat in _currentLayout.Seats)
        {
            if (!seat.IsAvailable) continue;
            var (cx , cy) = SeatGeometryHelper.GetPosition(seat , metadata);
            cx *= spread; cy *= spread;
            if (seat is PolarSeat) { cx -= baseW / 2; cy -= baseH / 2; }
            rawPositions.Add((cx , cy , seat));
            minX0 = Math.Min(minX0 , cx);
            minY0 = Math.Min(minY0 , cy);
            maxX0 = Math.Max(maxX0 , cx + baseW);
            maxY0 = Math.Max(maxY0 , cy + baseH);
        }
        // 将障碍物也纳入包围盒
        foreach (var obs in _currentLayout.Obstacles)
        {
            double ow = (obs.Width > 0 ? obs.Width : 60) * spread;
            double oh = (obs.Height > 0 ? obs.Height : 40) * spread;
            double ox = obs.X * spread;
            double oy = obs.Y * spread;
            minX0 = Math.Min(minX0 , ox);
            minY0 = Math.Min(minY0 , oy);
            maxX0 = Math.Max(maxX0 , ox + ow);
            maxY0 = Math.Max(maxY0 , oy + oh);
        }

        // 始终以当前原始坐标中心为参考中心（首次或重置后都正确）
        _contentCenterX = (minX0 + maxX0) / 2;
        _contentCenterY = (minY0 + maxY0) / 2;

        // 第二遍：以中心缩放，座位尺寸固定
        double seatWidth = baseW;
        double seatHeight = baseH;

        var items = new List<SeatDisplayItem>();
        int seatCounter = 0;

        foreach (var (cx , cy , seat) in rawPositions)
        {
            var occupantId = assignments.GetValueOrDefault(seat.Id);
            bool isOccupied = occupantId != null;
            bool isFrontRow = IsFrontRowSeat(seat , metadata);

            double sx = _contentCenterX + ((cx - _contentCenterX) * ZoomLevel);
            double sy = _contentCenterY + ((cy - _contentCenterY) * ZoomLevel);

            seatCounter++;
            items.Add(new SeatDisplayItem
            {
                X = sx ,
                Y = sy ,
                Width = seatWidth ,
                Height = seatHeight ,
                SeatId = seat.Id ,
                SeatLabel = BuildSeatLabel(seat , seatCounter) ,
                IsFrontRow = isFrontRow ,
                StudentName = isOccupied ? studentMap.GetValueOrDefault(occupantId! , "") : null ,
                StudentId = occupantId ,
                IsOccupied = isOccupied ,
                IsFixed = seat.IsFixed ,
                OccupancyStatus = isOccupied
                    ? (seat.IsFixed ? SeatOccupancyStatus.Fixed : SeatOccupancyStatus.Occupied)
                    : SeatOccupancyStatus.Empty
            });
        }

        // Canvas 大小 + 居中偏移
        double margin = 120;
        CanvasWidth = Math.Max(900 , ((maxX0 - minX0) * ZoomLevel) + (margin * 2));
        CanvasHeight = Math.Max(700 , ((maxY0 - minY0) * ZoomLevel) + (margin * 2));
        double offsetX = (CanvasWidth / 2) - _contentCenterX;
        double offsetY = (CanvasHeight / 2) - _contentCenterY;
        foreach (var item in items) { item.X += offsetX; item.Y += offsetY; }
        SeatItems = new ObservableCollection<SeatDisplayItem>(items);

        // 障碍物叠加层（大小按座宽比例动态计算）
        var overlays = new List<SeatDisplayItem>();
        double podiumW = baseW * 2.5;
        double podiumH = baseH * 1.6;
        double doorW = baseW * 1.2;
        double doorH = baseH * 0.9;

        // Grid 讲台 X 坐标：水平居中于座位范围
        double seatMinX = double.MaxValue, seatMaxX = 0;
        foreach (var seat in _currentLayout.Seats)
        {
            if (!seat.IsAvailable) continue;
            var (cx , _) = SeatGeometryHelper.GetPosition(seat , metadata);
            cx *= spread;
            seatMinX = Math.Min(seatMinX , cx);
            seatMaxX = Math.Max(seatMaxX , cx + baseW);
        }

        foreach (var obs in _currentLayout.Obstacles)
        {
            double w = obs.Width > 0 ? obs.Width : (obs.Type == "Podium" ? podiumW : doorW);
            double h = obs.Height > 0 ? obs.Height : (obs.Type == "Podium" ? podiumH : doorH);
            w *= spread; h *= spread;

            double obsX = obs.X * spread;
            double obsY = obs.Y * spread;
            // Grid 讲台强制居中（seatMin/Max 已含 spread）
            if (metadata is GridLayoutMetadata && obs.Type == "Podium" && seatMinX < seatMaxX)
                obsX = ((seatMinX + seatMaxX) / 2) - (w / 2);

            double ox = _contentCenterX + ((obsX - _contentCenterX) * ZoomLevel) + offsetX;
            double oy = _contentCenterY + ((obsY - _contentCenterY) * ZoomLevel) + offsetY;
            overlays.Add(new SeatDisplayItem
            {
                X = ox ,
                Y = oy ,
                Width = w ,
                Height = h ,
                SeatId = obs.Id ,
                SeatLabel = obs.Type ?? Resources.Seating_Obstacle ,
                CornerRadius = obs.Type == "Podium" ? new(w / 2) : new(4) ,
                OccupancyStatus = SeatOccupancyStatus.Empty
            });
        }
        OverlayItems = new ObservableCollection<SeatDisplayItem>(overlays);
    }

    private static (double width , double height) GetSeatDimensions (LayoutMetadata metadata)
        => ComputeSeatSize(metadata);

    /// <summary>按间距计算座位的安全尺寸（宽<最近邻间距的70%）。</summary>
    private static (double w , double h) ComputeSeatSize (LayoutMetadata metadata)
    {
        if (metadata is GridLayoutMetadata gm)
        {
            double intra = gm.IntraDeskSpacing > 0 ? gm.IntraDeskSpacing : 20;
            double inter = gm.InterDeskSpacing > 0 ? gm.InterDeskSpacing : 64;
            double colGap = gm.SeatsPerDesk > 1 ? Math.Min(intra , inter) : inter;
            double rowGap = gm.VerticalSpacing > 0 ? gm.VerticalSpacing : 56;
            double w = Math.Clamp(colGap * 0.95 , 44 , 72);
            double h = Math.Clamp(rowGap * 0.62 , 24 , 44);
            return (w , h);
        }
        if (metadata is PolarLayoutMetadata pm)
        {
            double step = pm.RadiusStep > 0 ? pm.RadiusStep : 40;
            double s = Math.Clamp(step * 0.85 , 28 , 48);
            return (s , s);
        }
        return (42 , 26);
    }

    // ── 座位标签与行列判断 ──

    private static string BuildSeatLabel (Seat seat , int counter)
    {
        return seat switch
        {
            GridSeat g => $"R{g.Row}C{g.Column}",
            PolarSeat p => string.Format(Resources.Seating_PolarLabelFmt, p.Ring, p.AngleDegrees),
            FreeformSeat => $"#{counter}",
            _ => $"#{counter}"
        };
    }

    private static bool IsFrontRowSeat (Seat seat , LayoutMetadata metadata)
    {
        return (seat , metadata) switch
        {
            (GridSeat g, GridLayoutMetadata gm) => g.Row <= gm.FrontRowCount,
            (PolarSeat p, PolarLayoutMetadata pm) => IsPolarFrontRow(p , pm),
            _ => false
        };
    }

    private static bool IsPolarFrontRow (PolarSeat seat , PolarLayoutMetadata meta)
    {
        int totalRings = meta.RingSeatCounts.Count > 0 ? meta.RingSeatCounts.Count : meta.Rings;
        int frontCount = Math.Min(meta.FrontRowCount , totalRings);
        return seat.Ring > (totalRings - frontCount);
    }

    // ── 显示刷新 ──

    private void RefreshSeatAssignments ()
    {
        if (_currentPlan == null || _workspace == null) return;
        var studentMap = _workspace.Students.ToDictionary(s => s.Id , s => s.Name);
        var assignments = _currentPlan.Assignments;

        foreach (var item in SeatItems)
        {
            var occupantId = assignments.GetValueOrDefault(item.SeatId);
            bool isOccupied = occupantId != null;
            item.StudentName = isOccupied ? studentMap.GetValueOrDefault(occupantId! , "") : null;
            item.StudentId = occupantId;
            item.IsOccupied = isOccupied;
            item.OccupancyStatus = isOccupied
                ? (item.IsFixed ? SeatOccupancyStatus.Fixed : SeatOccupancyStatus.Occupied)
                : SeatOccupancyStatus.Empty;
            item.IsSelectedForSwap = false;
        }
    }

    private async Task UpdateRightPanelAsync ()
    {
        // 策略列表
        var allStrategies = await _facade.GetStrategiesAsync();
        ActiveStrategies = new ObservableCollection<StrategyDisplayInfo>(
            allStrategies.Where(s => s.IsEnabled).OrderBy(s => s.Priority));

        // 未分配学生
        if (_workspace != null && _currentPlan != null)
        {
            var assignedIds = new HashSet<string>(_currentPlan.Assignments.Values.Where(v => v != null)!);
            UnassignedStudents = new ObservableCollection<Student>(
                _workspace.Students.Where(s => !assignedIds.Contains(s.Id)));
            OnPropertyChanged(nameof(UnassignedStudentCount));
        }
    }

    private void UpdateStats ()
    {
        TotalSeats = SeatItems.Count;
        AssignedSeats = SeatItems.Count(s => s.IsOccupied);
    }

    // ── 座位点击交换 ──

    [RelayCommand]
    private async Task ClickSeatAsync (SeatDisplayItem? clickedSeat)
    {
        if (clickedSeat == null || _workspace == null) return;

        // 首次点击：选择源座位
        if (_swapSourceSeat == null)
        {
            if (!clickedSeat.IsOccupied && !clickedSeat.IsFixed) return;

            _swapSourceSeat = clickedSeat;
            clickedSeat.IsSelectedForSwap = true;
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
                (source.SeatId , source.StudentId) ,
                (clickedSeat.SeatId , clickedSeat.IsOccupied ? clickedSeat.StudentId : null));

            var ok = await _facade.ExecuteCommandAsync(swapCmd);
            if (ok)
            {
                _currentPlan = _workspace!.BuildSeatingPlan();
                RefreshSeatAssignments();
                await UpdateRightPanelAsync();
                UpdateStats();
                AddHistoryEntry(string.Format(Resources.Seating_SwapDescFmt, source.StudentName ?? source.SeatLabel, clickedSeat.StudentName ?? Resources.Common_Cancel));
                StatusMessage = string.Format(Resources.Seating_SwappedFmt, source.StudentName, clickedSeat.StudentName ?? Resources.Common_Cancel);
            }
        } , Resources.Seating_SwapFailed);

        CancelSwap();
    }

    [RelayCommand]
    private void CancelSwap ()
    {
        if (_swapSourceSeat != null)
        {
            _swapSourceSeat.IsSelectedForSwap = false;
        }
        _swapSourceSeat = null;
        IsSwapMode = false;
        SwapHintText = string.Empty;
    }

    // ── 撤销/重做（基于历史列表） ──

    [RelayCommand]
    private void Undo ()
    {
        if (_workspace == null || _currentHistoryIndex <= 0) return;
        RestoreToHistoryIndex(_currentHistoryIndex - 1);
    }

    [RelayCommand]
    private void Redo ()
    {
        if (_workspace == null || _currentHistoryIndex >= _historyEntries.Count - 1) return;
        RestoreToHistoryIndex(_currentHistoryIndex + 1);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedHistory))]
    private HistoryEntry? _selectedHistory;

    public bool HasSelectedHistory => SelectedHistory != null;

    [RelayCommand]
    private void RestoreToSelected ()
    {
        if (SelectedHistory == null || _workspace == null) return;
        var idx = _historyEntries.IndexOf(SelectedHistory);
        if (idx < 0) return;
        RestoreToHistoryIndex(idx);
        StatusMessage = string.Format(Resources.Seating_RestoredToFmt, SelectedHistory.Description);
    }

    // ── 历史管理 ──

    private void InitHistory (string description)
    {
        _historyEntries.Clear();
        var snapshot = CaptureSnapshot();
        _historyEntries.Add(new HistoryEntry(description , snapshot));
        _currentHistoryIndex = 0;
        _lastSavedIndex = 0; // 生成时 facade 已自动保存快照
        UpdateHistoryState();
    }

    private void AddHistoryEntry (string description)
    {
        // 删除当前位置之后的所有条目（新分支）
        while (_historyEntries.Count > _currentHistoryIndex + 1)
            _historyEntries.RemoveAt(_historyEntries.Count - 1);

        var snapshot = CaptureSnapshot();
        _historyEntries.Add(new HistoryEntry(description , snapshot));
        _currentHistoryIndex = _historyEntries.Count - 1;
        UpdateHistoryState();
    }

    private Dictionary<string , string> CaptureSnapshot ()
        => _currentPlan != null ? new Dictionary<string , string>(_currentPlan.Assignments) : [];

    private void RestoreToHistoryIndex (int index)
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

    private void UpdateHistoryState ()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasHistory));
        // 更新各条目的 IsCurrent 标记
        for (int i = 0; i < _historyEntries.Count; i++)
            _historyEntries[i].IsCurrent = i == _currentHistoryIndex;
    }

    // ── 保存到快照 ──

    [RelayCommand]
    private async Task SaveToSnapshotAsync ()
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
        } , Resources.Seating_SnapshotFailed);
    }

    // ── 页面离开拦截 ──

    public override async Task<bool> CanLeaveAsync ()
    {
        if (!HasUnsavedChanges)
        {
            _facade.ClearWorkspace();
            return true;
        }

        var result = await Dialog.ShowMultiOptionAsync(Resources.Seating_UnsavedChanges ,
            Resources.Seating_UnsavedChangesMsg ,
            Resources.Seating_SaveAndLeave , Resources.Seating_DiscardAndLeave , "取消");

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
        return true;
    }

    // ── 折叠切换 ──

    [RelayCommand]
    private void ToggleStrategies () => IsStrategiesExpanded = !IsStrategiesExpanded;

    [RelayCommand]
    private void ToggleUnassigned () => IsUnassignedExpanded = !IsUnassignedExpanded;

    [RelayCommand]
    private void ToggleHistory () => IsHistoryExpanded = !IsHistoryExpanded;

    // ── 导出 ──

    private int _dialogLock;
    private static readonly TimeSpan ExportTimeout = TimeSpan.FromSeconds(30);

    [RelayCommand]
    private async Task ExportExcelAsync () => await ExportAsync(ExportFormat.Excel ,
        [new FilePickerFileType(Resources.Data_ExcelFile) { Patterns = ["*.xlsx"] }] , Resources.Seating_ExcelDefault);

    [RelayCommand]
    private async Task ExportCsvAsync () => await ExportAsync(ExportFormat.Csv ,
        [new FilePickerFileType(Resources.Data_CSVFile) { Patterns = ["*.csv"] }] , Resources.Seating_CsvDefault);

    [RelayCommand]
    private async Task ExportPdfAsync () => await ExportAsync(ExportFormat.Pdf ,
        [new FilePickerFileType(Resources.Seating_PDFFile) { Patterns = ["*.pdf"] }] , Resources.Seating_PDFDefault);

    [RelayCommand]
    private async Task ExportImageAsync () => await ExportAsync(ExportFormat.Png ,
        [new FilePickerFileType(Resources.Seating_PNGFile) { Patterns = ["*.png"] }] , Resources.Seating_PNGDefault);

    private async Task ExportAsync (ExportFormat format , IReadOnlyList<FilePickerFileType> types , string suggestedName)
    {
        if (Interlocked.CompareExchange(ref _dialogLock, 1, 0) != 0) return;
        try
        {
            if (_workspace == null) return;

            if (_currentLayout?.LayoutType == LayoutType.Freeform)
            {
                await Dialog.ShowWarningAsync(Resources.Seating_UnsupportedExport ,
                    Resources.Seating_UnsupportedExportMsg);
                return;
            }

            var file = await _fileService.SaveFileAsync(Resources.Seating_ExportTitle , types , suggestedName);
            if (file == null) return;

            var filePath = file.Path.LocalPath;
            var ok = await SafeExecuteAsync(async (ct) =>
            {
                var options = new ExportOptions { Format = format , IncludeMetadata = true };
                await _facade.ExportSeatingPlanAsync(_workspace , _currentLayout , filePath , options , ct);
                StatusMessage = string.Format(Resources.Seating_ExportedFmt, file.Name);
            } , ExportTimeout , Resources.Seating_ExportTitle);

            if (!ok)
            {
                try { File.Delete(filePath); } catch { /* ignore */ }
                StatusMessage = Resources.Seating_ExportTimeout;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "导出文件对话框取消或异常");
        }
        Interlocked.Exchange(ref _dialogLock, 0);
        await Task.Delay(150);
    }
}

public partial class HistoryEntry (string description , Dictionary<string , string> assignments) : ObservableObject
{
    public string Description { get; set; } = description;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public Dictionary<string , string> Assignments { get; set; } = assignments;
    public bool IsCurrent { get; set; }
    [ObservableProperty]
    private bool _isSelected;
}
