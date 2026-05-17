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
using A_Pair.Presentation.Avalonia.Services;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class SeatingArrangementViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly IFileService _fileService;

    // ── 内部状态 ──
    private SeatingWorkspace? _workspace;
    private ClassroomLayoutDefinition? _currentLayout;
    private SeatingPlan? _currentPlan;
    private SeatDisplayItem? _swapSourceSeat;
    private int _undoCount;
    private int _redoCount;
    private CancellationTokenSource? _generateCts;

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
    public double ZoomLevel { get; set; } = 1.0;
    public void ApplyZoom(double delta) { ZoomLevel = Math.Clamp(ZoomLevel + delta, 0.2, 3.0); BuildSeatDisplayItems(); }

    // ── 工具栏 ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    private bool _isGenerating;

    [ObservableProperty]
    private bool _hasGenerated;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    public bool CanGenerate => HasSelectedVenue && HasSelectedDataset && !IsGenerating;

    // ── 右侧面板 ──
    [ObservableProperty]
    private ObservableCollection<StrategyDisplayInfo> _activeStrategies = [];

    [ObservableProperty]
    private ObservableCollection<Student> _unassignedStudents = [];

    // ── 状态栏 ──
    [ObservableProperty]
    private string _statusMessage = "就绪";

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

    public SeatingArrangementViewModel(IApplicationFacade facade, IFileService fileService)
    {
        _facade = facade;
        _fileService = fileService;
        _ = LoadInitialDataAsync();
    }

    // ── 初始化 ──

    private async Task LoadInitialDataAsync()
    {
        await Task.WhenAll(LoadVenuesAsync(), LoadDatasetsAsync());
        StatusMessage = "就绪，请选择会场和学生数据集后生成座位安排";
    }

    public async Task RefreshDataAsync()
    {
        await Task.WhenAll(LoadVenuesAsync(), LoadDatasetsAsync());
    }

    [RelayCommand]
    private async Task LoadVenuesAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var ids = (await _facade.ListVenueIdsAsync()).ToList();
            var items = new List<VenueItem>();
            foreach (var id in ids)
            {
                var layout = await _facade.LoadVenueAsync(id);
                items.Add(new VenueItem(id, layout?.Name ?? id));
            }
            VenueItems = new ObservableCollection<VenueItem>(items);
        });
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

    // ── 会场选择 ──
    partial void OnSelectedVenueChanged(VenueItem? value)
    {
        if (value == null) return;
        _ = SafeExecuteAsync(async () =>
        {
            _currentLayout = await _facade.LoadVenueAsync(value.Id);
            if (_currentLayout != null)
            {
                ObstacleProcessor.ApplyObstacles(_currentLayout);
                StatusMessage = $"已选择会场「{_currentLayout.Name}」，共 {_currentLayout.Seats.Count} 个座位";
            }
        });
    }

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
        StatusMessage = "正在生成座位安排...";

        await SafeExecuteAsync(async () =>
        {
            // 1. 加载学生
            var students = await _facade.LoadStudentDatasetAsync(SelectedDataset!.Id, ct);
            if (students == null || students.Count == 0)
            {
                StatusMessage = "数据集中没有学生数据";
                return;
            }

            // 2. 写入临时 JSON 文件（RosterFile 格式）
            var roster = new RosterFile { Version = "1.0", Students = students };
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
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
                    StudentDataSource = tempPath,
                    Description = $"会场「{SelectedVenue.Name}」× 数据集「{SelectedDataset.Name}」"
                };

                _workspace = await _facade.GenerateSeatingAsync(request, progress, ct);
                _currentPlan = _workspace.BuildSeatingPlan();

                // 4. 构建显示
                BuildSeatDisplayItems();
                await UpdateRightPanelAsync();
                UpdateStats();

                _undoCount = 0;
                _redoCount = 0;
                CanUndo = false;
                CanRedo = false;
                HasGenerated = true;
                StatusMessage = $"座位安排已生成：{AssignedSeats}/{TotalSeats} 已分配";
            }
            finally
            {
                // 5. 清理临时文件
                try { File.Delete(tempPath); } catch { /* 忽略 */ }
            }
        }, "生成座位安排失败");

        IsGenerating = false;
    }

    // ── Canvas 数据构建 ──

    private void BuildSeatDisplayItems()
    {
        if (_currentLayout == null || _workspace == null || _currentPlan == null) return;

        var metadata = _currentLayout.Metadata;
        var studentMap = _workspace.Students.ToDictionary(s => s.Id, s => s.Name);
        var assignments = _currentPlan.Assignments;
        var (baseW, baseH) = GetSeatDimensions(metadata);

        // 网格布局扩距系数（增大间距防重叠）
        double spread = metadata is GridLayoutMetadata ? 1.35 : 1.0;

        // 第一遍：收集原始坐标范围
        double minX0 = double.MaxValue, minY0 = double.MaxValue;
        double maxX0 = 0, maxY0 = 0;
        var rawPositions = new List<(double cx, double cy, Seat seat)>();
        foreach (var seat in _currentLayout.Seats)
        {
            if (!seat.IsAvailable) continue;
            var (cx, cy) = SeatGeometryHelper.GetPosition(seat, metadata);
            cx *= spread; cy *= spread;
            if (seat is PolarSeat) { cx -= baseW / 2; cy -= baseH / 2; }
            rawPositions.Add((cx, cy, seat));
            minX0 = Math.Min(minX0, cx);
            minY0 = Math.Min(minY0, cy);
            maxX0 = Math.Max(maxX0, cx + baseW);
            maxY0 = Math.Max(maxY0, cy + baseH);
        }
        // 将障碍物也纳入包围盒
        foreach (var obs in _currentLayout.Obstacles)
        {
            double ow = (obs.Width > 0 ? obs.Width : 60) * spread;
            double oh = (obs.Height > 0 ? obs.Height : 40) * spread;
            double ox = obs.X * spread;
            double oy = obs.Y * spread;
            minX0 = Math.Min(minX0, ox);
            minY0 = Math.Min(minY0, oy);
            maxX0 = Math.Max(maxX0, ox + ow);
            maxY0 = Math.Max(maxY0, oy + oh);
        }

        // 始终以当前原始坐标中心为参考中心（首次或重置后都正确）
        _contentCenterX = (minX0 + maxX0) / 2;
        _contentCenterY = (minY0 + maxY0) / 2;

        // 第二遍：以中心缩放，座位尺寸固定
        double seatWidth = baseW;
        double seatHeight = baseH;

        var items = new List<SeatDisplayItem>();
        int seatCounter = 0;

        foreach (var (cx, cy, seat) in rawPositions)
        {
            var occupantId = assignments.GetValueOrDefault(seat.Id);
            bool isOccupied = occupantId != null;
            bool isFrontRow = IsFrontRowSeat(seat, metadata);

            double sx = _contentCenterX + (cx - _contentCenterX) * ZoomLevel;
            double sy = _contentCenterY + (cy - _contentCenterY) * ZoomLevel;

            seatCounter++;
            items.Add(new SeatDisplayItem
            {
                X = sx, Y = sy, Width = seatWidth, Height = seatHeight,
                SeatId = seat.Id,
                SeatLabel = BuildSeatLabel(seat, seatCounter),
                IsFrontRow = isFrontRow,
                StudentName = isOccupied ? studentMap.GetValueOrDefault(occupantId!, "") : null,
                StudentId = occupantId,
                IsOccupied = isOccupied,
                IsFixed = seat.IsFixed,
                OccupancyStatus = isOccupied
                    ? (seat.IsFixed ? SeatOccupancyStatus.Fixed : SeatOccupancyStatus.Occupied)
                    : SeatOccupancyStatus.Empty
            });
        }

        // Canvas 大小 + 居中偏移
        double margin = 120;
        CanvasWidth = Math.Max(900, (maxX0 - minX0) * ZoomLevel + margin * 2);
        CanvasHeight = Math.Max(700, (maxY0 - minY0) * ZoomLevel + margin * 2);
        double offsetX = CanvasWidth / 2 - _contentCenterX;
        double offsetY = CanvasHeight / 2 - _contentCenterY;
        foreach (var item in items) { item.X += offsetX; item.Y += offsetY; }
        SeatItems = new ObservableCollection<SeatDisplayItem>(items);

        // 障碍物叠加层
        var overlays = new List<SeatDisplayItem>();
        foreach (var obs in _currentLayout.Obstacles)
        {
            double w = (obs.Width > 0 ? obs.Width : 60) * spread;
            double h = (obs.Height > 0 ? obs.Height : 40) * spread;
            double ox = _contentCenterX + (obs.X * spread - _contentCenterX) * ZoomLevel + offsetX;
            double oy = _contentCenterY + (obs.Y * spread - _contentCenterY) * ZoomLevel + offsetY;
            overlays.Add(new SeatDisplayItem
            {
                X = ox, Y = oy, Width = w, Height = h,
                SeatId = obs.Id,
                SeatLabel = obs.Type ?? "障碍物",
                CornerRadius = obs.Type == "Podium" ? new(w / 2) : new(4),
                OccupancyStatus = SeatOccupancyStatus.Empty
            });
        }
        OverlayItems = new ObservableCollection<SeatDisplayItem>(overlays);
    }

    private static (double width, double height) GetSeatDimensions(LayoutMetadata metadata)
        => ComputeSeatSize(metadata);

    /// <summary>按间距计算座位的安全尺寸（宽<最近邻间距的70%）。</summary>
    private static (double w, double h) ComputeSeatSize(LayoutMetadata metadata)
    {
        if (metadata is GridLayoutMetadata gm)
        {
            double minGap = double.MaxValue;
            // 取实际列间距决定座宽（同桌 intra，桌间 inter，取较小值）
            double intra = gm.IntraDeskSpacing > 0 ? gm.IntraDeskSpacing : 20;
            double inter = gm.InterDeskSpacing > 0 ? gm.InterDeskSpacing : 64;
            double colGap = gm.SeatsPerDesk > 1 ? Math.Min(intra, inter) : inter;
            double rowGap = gm.VerticalSpacing > 0 ? gm.VerticalSpacing : 56;
            double w = Math.Clamp(colGap * 0.92, 38, 68);
            double h = Math.Clamp(rowGap * 0.6, 22, 42);
            return (w, h);
        }
        if (metadata is PolarLayoutMetadata pm)
        {
            double step = pm.RadiusStep > 0 ? pm.RadiusStep : 40;
            double s = Math.Clamp(step * 0.85, 28, 48);
            return (s, s);
        }
        return (36, 24);
    }

    // ── 座位标签与行列判断 ──

    private static string BuildSeatLabel(Seat seat, int counter)
    {
        return seat switch
        {
            GridSeat g => $"R{g.Row}C{g.Column}",
            PolarSeat p => $"环{p.Ring}-{p.AngleDegrees:F0}°",
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
    }

    private async Task UpdateRightPanelAsync()
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

    private void UpdateStats()
    {
        TotalSeats = SeatItems.Count;
        AssignedSeats = SeatItems.Count(s => s.IsOccupied);
    }

    // ── 座位点击交换 ──

    [RelayCommand]
    private async Task ClickSeatAsync(SeatDisplayItem? clickedSeat)
    {
        if (clickedSeat == null || _workspace == null) return;

        // 首次点击：选择源座位
        if (_swapSourceSeat == null)
        {
            if (!clickedSeat.IsOccupied && !clickedSeat.IsFixed) return;

            _swapSourceSeat = clickedSeat;
            clickedSeat.IsSelectedForSwap = true;
            IsSwapMode = true;
            SwapHintText = $"已选择「{clickedSeat.StudentName ?? clickedSeat.SeatLabel}」，请点击目标座位交换";
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

            var ok = await _facade.ExecuteCommandAsync(swapCmd);
            if (ok)
            {
                _currentPlan = _workspace!.BuildSeatingPlan();
                RefreshSeatAssignments();
                await UpdateRightPanelAsync();
                UpdateStats();
                _undoCount++;
                _redoCount = 0;
                CanUndo = _undoCount > 0;
                CanRedo = false;
                StatusMessage = $"已交换：{source.StudentName} ↔ {clickedSeat.StudentName ?? "(空位)"}";
            }
        }, "座位交换失败");

        CancelSwap();
    }

    [RelayCommand]
    private void CancelSwap()
    {
        if (_swapSourceSeat != null)
        {
            _swapSourceSeat.IsSelectedForSwap = false;
        }
        _swapSourceSeat = null;
        IsSwapMode = false;
        SwapHintText = string.Empty;
    }

    // ── 撤销/重做 ──

    [RelayCommand]
    private async Task UndoAsync()
    {
        if (_workspace == null) return;

        await SafeExecuteAsync(async () =>
        {
            var ok = await _facade.UndoAsync();
            if (ok)
            {
                _currentPlan = _workspace.BuildSeatingPlan();
                RefreshSeatAssignments();
                await UpdateRightPanelAsync();
                UpdateStats();
                _undoCount--;
                _redoCount++;
                CanUndo = _undoCount > 0;
                CanRedo = _redoCount > 0;
                StatusMessage = "已撤销";
            }
        });
    }

    [RelayCommand]
    private async Task RedoAsync()
    {
        if (_workspace == null) return;

        await SafeExecuteAsync(async () =>
        {
            var ok = await _facade.RedoAsync();
            if (ok)
            {
                _currentPlan = _workspace.BuildSeatingPlan();
                RefreshSeatAssignments();
                await UpdateRightPanelAsync();
                UpdateStats();
                _undoCount++;
                _redoCount--;
                CanUndo = _undoCount > 0;
                CanRedo = _redoCount > 0;
                StatusMessage = "已重做";
            }
        });
    }

    // ── 导出 ──

    [RelayCommand]
    private async Task ExportExcelAsync() => await ExportAsync(ExportFormat.Excel,
        [new FilePickerFileType("Excel 文件") { Patterns = ["*.xlsx"] }], "座位安排.xlsx");

    [RelayCommand]
    private async Task ExportCsvAsync() => await ExportAsync(ExportFormat.Csv,
        [new FilePickerFileType("CSV 文件") { Patterns = ["*.csv"] }], "座位安排.csv");

    [RelayCommand]
    private async Task ExportPdfAsync() => await ExportAsync(ExportFormat.Pdf,
        [new FilePickerFileType("PDF 文件") { Patterns = ["*.pdf"] }], "座位安排.pdf");

    [RelayCommand]
    private async Task ExportImageAsync() => await ExportAsync(ExportFormat.Png,
        [new FilePickerFileType("PNG 图片") { Patterns = ["*.png"] }], "座位安排.png");

    private async Task ExportAsync(ExportFormat format, IReadOnlyList<FilePickerFileType> types, string suggestedName)
    {
        if (_workspace == null) return;

        if (_currentLayout?.LayoutType == LayoutType.Freeform)
        {
            await Dialog.ShowWarningAsync("不支持的数据导出" ,
                "自由点布局不支持导出为表格格式，请使用「预览图片」导出。");
            return;
        }

        var file = await _fileService.SaveFileAsync("导出座位安排", types, suggestedName);
        if (file == null) return;

        await SafeExecuteAsync(async () =>
        {
            var options = new ExportOptions { Format = format, IncludeMetadata = true };
            await _facade.ExportSeatingPlanAsync(_workspace, _currentLayout, file.Path.LocalPath, options);
            StatusMessage = $"已导出至 {file.Name}";
        }, "导出失败");
    }
}
