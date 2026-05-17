using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.DomainServices;
using A_Pair.Core.Models;
using A_Pair.Infrastructure.Layouts;
using A_Pair.Presentation.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class VenueConfigurationViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly INavigationService _navigation;

    public string Title { get; } = "会场配置";

    [ObservableProperty]
    private ObservableCollection<VenueItem> _venueItems = [];

    private bool _suppressAutoLoad;
    private CancellationTokenSource? _selectVenueCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVenue))]
    [NotifyPropertyChangedFor(nameof(SelectedVenueId))]
    private VenueItem? _selectedVenueItem;

    public string? SelectedVenueId => SelectedVenueItem?.Id;
    public bool HasSelectedVenue => SelectedVenueItem != null;

    [ObservableProperty]
    private string _layoutName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridSelected))]
    [NotifyPropertyChangedFor(nameof(IsPolarSelected))]
    [NotifyPropertyChangedFor(nameof(IsFreeformSelected))]
    private LayoutType _selectedLayoutType = LayoutType.Grid;

    public bool IsGridSelected => SelectedLayoutType == LayoutType.Grid;
    public bool IsPolarSelected => SelectedLayoutType == LayoutType.Polar;
    public bool IsFreeformSelected => SelectedLayoutType == LayoutType.Freeform;
    public bool IsDoorPanelVisible => IsGridSelected || IsPolarSelected || IsFreeformSelected;

    [ObservableProperty]
    private bool _isFreeformVenue;

    public bool CanChangeLayoutType => !IsFreeformVenue;

    partial void OnIsFreeformVenueChanged (bool value)
    {
        OnPropertyChanged(nameof(CanChangeLayoutType));
    }

    private List<FreeformSeat> _freeformPreviewSeats = [];
    private List<Obstacle> _freeformPreviewObstacles = [];

    // ── 侧边栏折叠 ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarCollapsed))]
    private bool _isSidebarExpanded = true;

    public bool IsSidebarCollapsed => !IsSidebarExpanded;

    [ObservableProperty]
    private double _sidebarListWidth = 240;

    private bool _userWantsSidebarExpanded = true;

    public void OnWindowWidthChanged (double windowWidth)
    {
        if (windowWidth < 750)
            IsSidebarExpanded = false;
        else
            IsSidebarExpanded = _userWantsSidebarExpanded;
    }

    partial void OnIsSidebarExpandedChanged (bool value)
        => SidebarListWidth = value ? 240 : 78;

    [RelayCommand]
    private void ToggleSidebar ()
    {
        _userWantsSidebarExpanded = !_userWantsSidebarExpanded;
        IsSidebarExpanded = _userWantsSidebarExpanded;
    }

    // ── Grid 基础参数 ──
    [ObservableProperty] private int _gridRows = 5;
    [ObservableProperty] private int _gridColumns = 8;
    [ObservableProperty] private double _gridHorizontalSpacing = 40;
    [ObservableProperty] private double _gridVerticalSpacing = 36;
    [ObservableProperty] private double _gridOriginX = 200;
    [ObservableProperty] private double _gridOriginY = 200;

    // ── Grid 桌面配置 ──
    [ObservableProperty] private int _gridSeatsPerDesk = 2;
    [ObservableProperty] private double _gridIntraDeskSpacing = 12;
    [ObservableProperty] private double _gridInterDeskSpacing = 40;

    // ── Grid 过道配置 ──
    [ObservableProperty] private string _gridAisleAfterColumns = "";
    [ObservableProperty] private string _gridAisleAfterRows = "";
    [ObservableProperty] private double _gridAisleWidth = 60;
    [ObservableProperty] private ObservableCollection<AisleOption> _aisleColumnOptions = [];
    [ObservableProperty] private ObservableCollection<AisleOption> _aisleRowOptions = [];

    // ── Grid 教室特征 ──
    [ObservableProperty] private int _gridFrontRowCount = 1;
    [ObservableProperty] private bool _gridHasPodium = true;
    [ObservableProperty] private bool _gridHasFrontDoor;
    [ObservableProperty] private double _gridPodiumWidth = 60;
    [ObservableProperty] private double _gridPodiumHeight = 40;

    // ── Grid 每列行数 & 禁用座位 ──
    [ObservableProperty] private string _gridColumnRowCountsSpec = "";
    [ObservableProperty] private string _gridEmptyPositionsSpec = "";

    // ── 门配置（支持多门、自定义位置）──
    [ObservableProperty] private ObservableCollection<DoorItem> _doorItems = [];

    // ── Polar 参数 ──
    [ObservableProperty] private int _polarRings = 3;
    [ObservableProperty] private int _polarSeatsPerRing = 12;
    [ObservableProperty] private double _polarRadiusStep = 40;
    [ObservableProperty] private double _polarStartAngle = 0;
    [ObservableProperty] private double _polarEndAngle = 360;
    [ObservableProperty] private double _polarOriginX = 200;
    [ObservableProperty] private double _polarOriginY = 200;
    [ObservableProperty] private string _polarRingSeatCountsSpec = "";   // 每环座位数逗号分隔，空=用均匀环
    [ObservableProperty] private string _polarEmptyPositionsSpec = "";   // 禁用座位：分号分隔的 Ring,Angle 对
    [ObservableProperty] private bool _polarHasPodium = true;
    [ObservableProperty] private double _polarPodiumRadius = 30;
    [ObservableProperty] private string _polarAisleRadialAngles = "";
    [ObservableProperty] private double _polarAisleRadialWidth = 5;
    [ObservableProperty] private string _polarAisleCircularRings = "";
    [ObservableProperty] private double _polarAisleCircularWidth = 20;
    [ObservableProperty] private int _polarFrontRowCount = 1;

    // ── 预览 ──
    [ObservableProperty] private ObservableCollection<SeatPreview> _previewSeats = [];
    [ObservableProperty] private ObservableCollection<SeatPreview> _previewOverlays = [];
    [ObservableProperty] private double _canvasWidth = 600;
    [ObservableProperty] private double _canvasHeight = 600;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public VenueConfigurationViewModel (IApplicationFacade facade , IDialogService dialog , INavigationService navigation)
    {
        _facade = facade;
        _navigation = navigation;
        _ = LoadVenueList();
        RegenerateAisleOptions();
    }

    // ═══════════════════════════════════════════════
    // 会场列表
    // ═══════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadVenueList ()
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
            StatusMessage = $"已加载 {items.Count} 个会场";
        });
    }

    [RelayCommand]
    private void NewVenue ()
    {
        _suppressAutoLoad = true;
        var id = Guid.NewGuid().ToString("N")[..8];
        var item = new VenueItem(id , $"新会场_{id}");
        LayoutName = item.Name;
        IsFreeformVenue = false;
        SelectedLayoutType = LayoutType.Grid;
        ResetParameters();
        VenueItems.Add(item);
        SelectedVenueItem = item;
        RegeneratePreview();
        StatusMessage = "已创建新会场，请编辑参数后保存";
        _suppressAutoLoad = false;
    }

    [RelayCommand]
    private async Task DeleteVenue ()
    {
        if (SelectedVenueItem == null) return;
        var item = SelectedVenueItem;
        var confirmed = await Dialog.ShowConfirmAsync("确认删除" , $"确定要删除会场「{item.Name}」吗？此操作不可恢复。");
        if (!confirmed) return;

        await SafeExecuteAsync(async () =>
        {
            await _facade.DeleteVenueAsync(item.Id);
            SelectedVenueItem = null;
            PreviewSeats.Clear();
            PreviewOverlays.Clear();
            LayoutName = string.Empty;
            await LoadVenueList();
            StatusMessage = $"会场「{item.Name}」已删除";
        } , "删除会场失败");
    }

    private async Task SelectVenueAsync (VenueItem item , CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await SafeExecuteAsync(async () =>
        {
            var layout = await _facade.LoadVenueAsync(item.Id);
            if (layout == null) { StatusMessage = $"加载会场「{item.Name}」失败"; return; }

            LayoutName = layout.Name;
            SelectedLayoutType = layout.LayoutType;
            IsFreeformVenue = layout.LayoutType == LayoutType.Freeform;

            switch (layout.Metadata)
            {
                case GridLayoutMetadata g:
                    PopulateGridFromMetadata(g);
                    break;
                case PolarLayoutMetadata p:
                    PopulatePolarFromMetadata(p);
                    break;
                case FreeformLayoutMetadata:
                    _freeformPreviewSeats = layout.Seats.OfType<FreeformSeat>().ToList();
                    _freeformPreviewObstacles = layout.Obstacles.ToList();
                    break;
            }

            // 恢复障碍物配置（门等）
            RestoreObstaclesFromLayout(layout);

            RegeneratePreview();
            StatusMessage = $"已加载会场「{layout.Name}」，共 {layout.Seats.Count} 个座位";
        });
    }

    [RelayCommand]
    private async Task SaveVenue ()
    {
        if (SelectedVenueItem == null) return;
        var item = SelectedVenueItem;

        await SafeExecuteAsync(async () =>
        {
            var layout = BuildLayoutDefinition();
            await _facade.SaveVenueAsync(item.Id , layout);
            await LoadVenueList();
            SelectedVenueItem = VenueItems.FirstOrDefault(v => v.Id == item.Id);
            StatusMessage = $"会场「{layout.Name}」已保存，共 {layout.Seats.Count} 个座位";
        } , "保存会场失败");
    }

    [RelayCommand]
    private void SelectLayoutType (string type)
    {
        if (IsFreeformVenue) return;
        SelectedLayoutType = type switch
        {
            "Polar" => LayoutType.Polar,
            "Freeform" => LayoutType.Freeform,
            _ => LayoutType.Grid,
        };
    }

    [RelayCommand]
    private void AddDoor ()
    {
        double dx = GridOriginX - 50;
        double dy = GridOriginY - 20;
        DoorItems.Add(new DoorItem(dx , dy , $"门 {DoorItems.Count + 1}"));
    }

    [RelayCommand]
    private void RemoveDoor (DoorItem door)
    {
        DoorItems.Remove(door);
    }

    [RelayCommand]
    private async Task NavigateToFreeformAsync () => await _navigation.NavigateToAsync(PageKey.FreeformManagement);

    // ═══════════════════════════════════════════════
    // 预览 & 构建
    // ═══════════════════════════════════════════════

    [RelayCommand]
    private void RegeneratePreview ()
    {
        var seats = new List<SeatPreview>();
        var overlays = new List<SeatPreview>();

        if (SelectedLayoutType == LayoutType.Grid)
        {
            var meta = BuildGridMetadata();
            var layout = GridLayoutBuilder.BuildGrid(meta);

            double SpacingTimes = 2.0;

            // 仅放大同桌间距（IntraDeskSpacing），桌间和行间保持原样

            var previewMeta = new GridLayoutMetadata
            {
                Rows = meta.Rows ,
                Columns = meta.Columns ,
                OriginX = meta.OriginX ,
                OriginY = meta.OriginY ,
                SeatsPerDesk = meta.SeatsPerDesk ,
                IntraDeskSpacing = meta.IntraDeskSpacing * SpacingTimes ,
                InterDeskSpacing = meta.InterDeskSpacing ,
                HorizontalSpacing = meta.HorizontalSpacing ,
                VerticalSpacing = meta.VerticalSpacing ,
                AisleAfterColumns = meta.AisleAfterColumns ,
                AisleAfterRows = meta.AisleAfterRows ,
                AisleWidth = meta.AisleWidth ,
                ColumnRowCounts = meta.ColumnRowCounts ,
                EmptyPositions = meta.EmptyPositions ,
            };
            double seatW = 20, seatH = 14;

            foreach (GridSeat s in layout.Seats.Cast<GridSeat>())
            {
                var (x , y) = SeatGeometryHelper.GetPosition(s , previewMeta);
                bool isFront = s.Row <= meta.FrontRowCount;
                int deskNum = ((s.Column - 1) / meta.SeatsPerDesk) + 1;
                seats.Add(new SeatPreview
                {
                    X = x ,
                    Y = y ,
                    Width = seatW ,
                    Height = seatH ,
                    Label = $"R{s.Row}C{s.Column} (桌{deskNum})" ,
                    ElementType = PreviewElementType.Seat ,
                    IsFrontRow = isFront
                });
            }

            // 讲台（水平居中于网格）
            if (meta.HasPodium && meta.PodiumWidth > 0 && meta.PodiumHeight > 0)
            {
                double gridLeft = seats.Min(s => s.X);
                double gridRight = seats.Max(s => s.X + s.Width);
                double podiumW = meta.PodiumWidth;
                double podiumH = meta.PodiumHeight;
                double podiumX = ((gridLeft + gridRight) / 2) - (podiumW / 2);
                double podiumY = meta.OriginY - meta.PodiumHeight - meta.VerticalSpacing;
                overlays.Add(new SeatPreview
                {
                    X = podiumX ,
                    Y = podiumY ,
                    Width = podiumW ,
                    Height = podiumH ,
                    ElementType = PreviewElementType.Podium ,
                    Label = "讲台"
                });
            }

            // 禁用座位标记（红色半透明）
            foreach (var empty in meta.EmptyPositions ?? [])
            {
                var virtualSeat = new GridSeat { Row = empty.Row , Column = empty.Column };
                var (ex , ey) = SeatGeometryHelper.GetPosition(virtualSeat , previewMeta);
                overlays.Add(new SeatPreview
                {
                    X = ex ,
                    Y = ey ,
                    Width = seatW ,
                    Height = seatH ,
                    Label = $"R{empty.Row}C{empty.Column} (禁用)" ,
                    ElementType = PreviewElementType.Aisle ,
                    BackgroundColor = "#80CC4444"
                });
            }
        }
        else if (SelectedLayoutType == LayoutType.Polar)
        {
            var meta = BuildPolarMetadata();
            var layout = PolarLayoutBuilder.BuildPolar(meta);
            int totalRings = meta.RingSeatCounts.Count > 0 ? meta.RingSeatCounts.Count : meta.Rings;

            double seatR = 7;  // 座位圆点半径
            foreach (PolarSeat s in layout.Seats.Cast<PolarSeat>())
            {
                var (cx , cy) = SeatGeometryHelper.GetPosition(s , meta);
                bool isFront = s.Ring > totalRings - meta.FrontRowCount;
                seats.Add(new SeatPreview
                {
                    X = cx - seatR ,
                    Y = cy - seatR ,
                    Width = seatR * 2 ,
                    Height = seatR * 2 ,
                    Label = $"R{s.Ring} {s.AngleDegrees:F0}° ({s.LogicalGroup})" ,
                    ElementType = PreviewElementType.Seat ,
                    IsFrontRow = isFront ,
                    CornerRadius = new(seatR) ,
                    IsCircle = true
                });
            }

            // 讲台（圆心处，完整圆）
            if (meta.HasPodium && meta.PodiumRadius > 0)
            {
                double pr = meta.PodiumRadius;
                overlays.Add(new SeatPreview
                {
                    X = meta.OriginX - pr ,
                    Y = meta.OriginY - pr ,
                    Width = pr * 2 ,
                    Height = pr * 2 ,
                    ElementType = PreviewElementType.Podium ,
                    Label = "讲台" ,
                    CornerRadius = new(pr) ,
                    IsCircle = true ,
                    BackgroundColor = "#4080D0E0"
                });
            }

            // 禁用座位标记（红色半透明圆点，复用上方 seatR 变量）
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
                    overlays.Add(new SeatPreview
                    {
                        X = cx - seatR ,
                        Y = cy - seatR ,
                        Width = seatR * 2 ,
                        Height = seatR * 2 ,
                        Label = $"R{empty.Ring} {empty.AngleDegrees:F0}° (禁用)" ,
                        ElementType = PreviewElementType.Aisle ,
                        CornerRadius = new(seatR) ,
                        IsCircle = true ,
                        BackgroundColor = "#80CC4444"
                    });
                }
            }

        }
        else if (SelectedLayoutType == LayoutType.Freeform)
        {
            double seatSize = 18;
            foreach (var s in _freeformPreviewSeats)
            {
                seats.Add(new SeatPreview
                {
                    X = s.X - (seatSize / 2) ,
                    Y = s.Y - (seatSize / 2) ,
                    Width = seatSize ,
                    Height = seatSize ,
                    Label = s.Row.HasValue && s.Column.HasValue
                        ? $"R{s.Row}C{s.Column}"
                        : $"({s.X:F0}, {s.Y:F0})" ,
                    ElementType = PreviewElementType.Seat ,
                    CornerRadius = new(seatSize / 2) ,
                    IsCircle = true
                });
            }

            foreach (var obs in _freeformPreviewObstacles.Where(o => o.Type != "Door"))
            {
                var elementType = obs.Type == "Podium" ? PreviewElementType.Podium : PreviewElementType.Obstacle;
                double w = obs.Width > 0 ? obs.Width : 60;
                double h = obs.Height > 0 ? obs.Height : 40;
                overlays.Add(new SeatPreview
                {
                    X = obs.X - (w / 2) ,
                    Y = obs.Y - (h / 2) ,
                    Width = w ,
                    Height = h ,
                    Label = obs.Type ?? "障碍物" ,
                    ElementType = elementType ,
                    CornerRadius = elementType == PreviewElementType.Podium ? new(w / 2) : new(4) ,
                    IsCircle = elementType == PreviewElementType.Podium ,
                    BackgroundColor = elementType == PreviewElementType.Podium ? "#4080D0E0" : "#60DD6666"
                });
            }
        }

        // 门（两种布局共用 DoorItems）
        foreach (var door in DoorItems)
        {
            overlays.Add(new SeatPreview
            {
                X = door.X ,
                Y = door.Y ,
                Width = 36 ,
                Height = 24 ,
                ElementType = PreviewElementType.Door ,
                Label = door.Label
            });
        }

        // 内容居中：margin 按内容范围的 25% 计算，最小 60px
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = 0, maxY = 0;
        foreach (var s in seats) { minX = Math.Min(minX , s.X); minY = Math.Min(minY , s.Y); maxX = Math.Max(maxX , s.X + s.Width); maxY = Math.Max(maxY , s.Y + s.Height); }
        foreach (var o in overlays) { minX = Math.Min(minX , o.X); minY = Math.Min(minY , o.Y); maxX = Math.Max(maxX , o.X + o.Width); maxY = Math.Max(maxY , o.Y + o.Height); }
        if (seats.Count > 0 || overlays.Count > 0)
        {
            double cw = maxX - minX, ch = maxY - minY;
            double mx = Math.Max(60 , cw * 0.25), my = Math.Max(60 , ch * 0.25);
            double ox = mx - minX, oy = my - minY;
            foreach (var s in seats) { s.X += ox; s.Y += oy; }
            foreach (var o in overlays) { o.X += ox; o.Y += oy; }
        }

        // 动态 Canvas 大小
        double canvasW = 600, canvasH = 600;
        if (seats.Count > 0 || overlays.Count > 0)
        {
            double cmaxX = 0, cmaxY = 0;
            foreach (var s in seats) { cmaxX = Math.Max(cmaxX , s.X + s.Width); cmaxY = Math.Max(cmaxY , s.Y + s.Height); }
            foreach (var o in overlays) { cmaxX = Math.Max(cmaxX , o.X + o.Width); cmaxY = Math.Max(cmaxY , o.Y + o.Height); }
            canvasW = Math.Max(600 , cmaxX + 40);
            canvasH = Math.Max(600 , cmaxY + 40);
        }
        CanvasWidth = canvasW;
        CanvasHeight = canvasH;

        PreviewSeats = new ObservableCollection<SeatPreview>(seats);
        PreviewOverlays = new ObservableCollection<SeatPreview>(overlays);
        StatusMessage = $"预览：{seats.Count} 个座位";
    }

    private void RegenerateAisleOptions ()
    {
        var prevCols = new HashSet<int>(ParseIntList(GridAisleAfterColumns));
        var prevRows = new HashSet<int>(ParseIntList(GridAisleAfterRows));
        int spd = GridSeatsPerDesk > 0 ? GridSeatsPerDesk : 1;

        // 列过道选项：以桌列为单位
        int deskCols = GridColumns / spd;
        var colOptions = new List<AisleOption>();
        for (int d = 1; d < deskCols; d++)
        {
            int seatCol = d * spd; // 过道在该座位列索引之后
            int leftStart = ((d - 1) * spd) + 1;
            int leftEnd = d * spd;
            int rightStart = (d * spd) + 1;
            int rightEnd = Math.Min((d + 1) * spd , GridColumns);
            string label = $"{leftStart}-{leftEnd} 列 ↔ {rightStart}-{rightEnd} 列";
            colOptions.Add(new AisleOption(label , seatCol , prevCols.Contains(seatCol)));
        }
        AisleColumnOptions = new ObservableCollection<AisleOption>(colOptions);
        foreach (var opt in AisleColumnOptions)
            opt.PropertyChanged += (_ , _) => SyncAisleColumnsFromOptions();

        // 行过道选项
        var rowOptions = new List<AisleOption>();
        for (int r = 1; r < GridRows; r++)
        {
            string label = $"第 {r} 排 ↔ 第 {r + 1} 排";
            rowOptions.Add(new AisleOption(label , r , prevRows.Contains(r)));
        }
        AisleRowOptions = new ObservableCollection<AisleOption>(rowOptions);
        foreach (var opt in AisleRowOptions)
            opt.PropertyChanged += (_ , _) => SyncAisleRowsFromOptions();
    }

    /// <summary>过道勾选状态变化时同步回字符串。</summary>
    private void SyncAisleColumnsFromOptions ()
    {
        var selected = AisleColumnOptions.Where(o => o.IsSelected).Select(o => o.SeatColumn);
        GridAisleAfterColumns = string.Join("," , selected);
    }

    private void SyncAisleRowsFromOptions ()
    {
        var selected = AisleRowOptions.Where(o => o.IsSelected).Select(o => o.SeatColumn);
        GridAisleAfterRows = string.Join("," , selected);
    }

    private ClassroomLayoutDefinition BuildLayoutDefinition ()
    {
        ClassroomLayoutDefinition layout;
        switch (SelectedLayoutType)
        {
            case LayoutType.Grid:
                var meta = BuildGridMetadata();
                layout = GridLayoutBuilder.BuildGrid(meta);
                layout.Name = LayoutName;
                layout.Id = SelectedVenueItem?.Id ?? "";
                // 将讲台/前门作为 Obstacle 写入（讲台居中于网格）
                if (meta.HasPodium && meta.PodiumWidth > 0 && meta.PodiumHeight > 0)
                {
                    double podiumW = meta.PodiumWidth;
                    double gridMidX = layout.Seats.Count > 0
                        ? (layout.Seats.Min(s => s is GridSeat g ? SeatGeometryHelper.GetPosition(s , meta).X : 0)
                         + layout.Seats.Max(s => s is GridSeat g ? SeatGeometryHelper.GetPosition(s , meta).X : 0)) / 2
                        : meta.OriginX;
                    layout.Obstacles.Add(new Obstacle
                    {
                        X = gridMidX - (podiumW / 2) ,
                        Y = meta.OriginY - meta.PodiumHeight - meta.VerticalSpacing ,
                        Width = podiumW ,
                        Height = meta.PodiumHeight ,
                        Type = "Podium"
                    });
                }
                foreach (var door in DoorItems)
                {
                    layout.Obstacles.Add(new Obstacle
                    {
                        X = door.X ,
                        Y = door.Y ,
                        Width = 36 ,
                        Height = 24 ,
                        Type = "Door"
                    });
                }
                break;

            case LayoutType.Polar:
                var polarMeta = BuildPolarMetadata();
                layout = PolarLayoutBuilder.BuildPolar(polarMeta);
                layout.Name = LayoutName;
                layout.Id = SelectedVenueItem?.Id ?? "";
                foreach (var door in DoorItems)
                {
                    layout.Obstacles.Add(new Obstacle
                    {
                        X = door.X ,
                        Y = door.Y ,
                        Width = 36 ,
                        Height = 24 ,
                        Type = "Door"
                    });
                }
                break;

            case LayoutType.Freeform:
                var seatPoints = _freeformPreviewSeats
                    .Select(s => (s.X , s.Y , (int?)s.Row , (int?)s.Column , GroupId: (int?)null))
                    .ToList();
                var obstaclePoints = _freeformPreviewObstacles
                    .Where(o => o.Type != "Door")
                    .Select(o => (o.X , o.Y , Math.Max(o.Width , 60) , Math.Max(o.Height , 40) , o.Type ?? "Podium"))
                    .ToList();
                foreach (var d in DoorItems)
                    obstaclePoints.Add((d.X , d.Y , 36.0 , 24.0 , "Door"));
                layout = FreeformLayoutBuilder.BuildFreeform(
                    seatPoints , obstaclePoints.Count > 0 ? obstaclePoints : null);
                layout.Id = SelectedVenueItem?.Id ?? "";
                layout.Name = LayoutName;
                break;

            default:
                layout = new ClassroomLayoutDefinition { Name = LayoutName };
                break;
        }

        return layout;
    }

    // ═══════════════════════════════════════════════
    // 辅助
    // ═══════════════════════════════════════════════

    private GridLayoutMetadata BuildGridMetadata ()
    {
        return new GridLayoutMetadata
        {
            Rows = GridRows ,
            Columns = GridColumns ,
            HorizontalSpacing = GridHorizontalSpacing ,
            VerticalSpacing = GridVerticalSpacing ,
            OriginX = GridOriginX ,
            OriginY = GridOriginY ,
            SeatsPerDesk = GridSeatsPerDesk ,
            IntraDeskSpacing = GridIntraDeskSpacing ,
            InterDeskSpacing = GridInterDeskSpacing ,
            AisleAfterColumns = ParseIntList(GridAisleAfterColumns) ,
            AisleAfterRows = ParseIntList(GridAisleAfterRows) ,
            AisleWidth = GridAisleWidth ,
            FrontRowCount = GridFrontRowCount ,
            HasPodium = GridHasPodium ,
            PodiumWidth = GridPodiumWidth ,
            PodiumHeight = GridPodiumHeight ,
            ColumnRowCounts = ParseIntList(GridColumnRowCountsSpec) ,
            EmptyPositions = ParseGridEmptyPositions(GridEmptyPositionsSpec)
        };
    }

    private PolarLayoutMetadata BuildPolarMetadata ()
    {
        return new PolarLayoutMetadata
        {
            Rings = PolarRings ,
            SeatsPerRing = PolarSeatsPerRing ,
            RadiusStep = PolarRadiusStep ,
            StartAngleDegrees = PolarStartAngle ,
            EndAngleDegrees = PolarEndAngle ,
            OriginX = PolarOriginX ,
            OriginY = PolarOriginY ,
            RingSeatCounts = ParseIntList(PolarRingSeatCountsSpec) ,
            HasPodium = PolarHasPodium ,
            PodiumRadius = PolarPodiumRadius ,
            AisleRadialAngles = ParseDoubleList(PolarAisleRadialAngles) ,
            AisleRadialWidthDegrees = PolarAisleRadialWidth ,
            AisleCircularAfterRings = ParseIntList(PolarAisleCircularRings) ,
            AisleCircularWidth = PolarAisleCircularWidth ,
            FrontRowCount = PolarFrontRowCount ,
            EmptyPositions = ParsePolarEmptyPositions(PolarEmptyPositionsSpec)
        };
    }

    private void RestoreObstaclesFromLayout (ClassroomLayoutDefinition layout)
    {
        var doors = layout.Obstacles.Where(o => o.Type == "Door").ToList();
        DoorItems = new ObservableCollection<DoorItem>(
            doors.Select((d , i) => new DoorItem(d.X , d.Y , $"门 {i + 1}")));

        if (layout.Metadata is GridLayoutMetadata gridMeta)
            GridHasFrontDoor = gridMeta.HasFrontDoor;
    }

    private void PopulateGridFromMetadata (GridLayoutMetadata g)
    {
        GridRows = g.Rows > 0 ? g.Rows : 5;
        GridColumns = g.Columns > 0 ? g.Columns : 8;
        GridHorizontalSpacing = g.HorizontalSpacing > 0 ? g.HorizontalSpacing : 40;
        GridVerticalSpacing = g.VerticalSpacing > 0 ? g.VerticalSpacing : 36;
        GridOriginX = g.OriginX > 0 ? g.OriginX : 200;
        GridOriginY = g.OriginY > 0 ? g.OriginY : 200;
        GridSeatsPerDesk = g.SeatsPerDesk > 0 ? g.SeatsPerDesk : 2;
        GridIntraDeskSpacing = g.IntraDeskSpacing > 0 ? g.IntraDeskSpacing : 12;
        GridInterDeskSpacing = g.InterDeskSpacing > 0 ? g.InterDeskSpacing : 40;
        GridAisleAfterColumns = string.Join("," , g.AisleAfterColumns ?? []);
        GridAisleAfterRows = string.Join("," , g.AisleAfterRows ?? []);
        GridAisleWidth = g.AisleWidth > 0 ? g.AisleWidth : 60;
        GridFrontRowCount = g.FrontRowCount > 0 ? g.FrontRowCount : 1;
        GridHasPodium = g.HasPodium;
        GridPodiumWidth = g.PodiumWidth > 0 ? g.PodiumWidth : 60;
        GridPodiumHeight = g.PodiumHeight > 0 ? g.PodiumHeight : 40;
        GridColumnRowCountsSpec = g.ColumnRowCounts is { Count: > 0 } ? string.Join("," , g.ColumnRowCounts) : "";
        GridEmptyPositionsSpec = g.EmptyPositions is { Count: > 0 }
            ? string.Join(";" , g.EmptyPositions.Select(p => $"{p.Row},{p.Column}"))
            : "";

        RegenerateAisleOptions();
    }

    private void PopulatePolarFromMetadata (PolarLayoutMetadata p)
    {
        PolarRings = p.Rings > 0 ? p.Rings : 3;
        PolarSeatsPerRing = p.SeatsPerRing > 0 ? p.SeatsPerRing : 12;
        PolarRadiusStep = p.RadiusStep > 0 ? p.RadiusStep : 40;
        PolarStartAngle = p.StartAngleDegrees;
        PolarEndAngle = p.EndAngleDegrees > 0 ? p.EndAngleDegrees : 360;
        PolarOriginX = p.OriginX > 0 ? p.OriginX : 200;
        PolarOriginY = p.OriginY > 0 ? p.OriginY : 200;
        PolarRingSeatCountsSpec = p.RingSeatCounts is { Count: > 0 } ? string.Join("," , p.RingSeatCounts) : "";
        PolarHasPodium = p.HasPodium;
        PolarPodiumRadius = p.PodiumRadius > 0 ? p.PodiumRadius : 30;
        PolarAisleRadialAngles = p.AisleRadialAngles is { Count: > 0 } ? string.Join("," , p.AisleRadialAngles.Select(a => a.ToString("F1"))) : "";
        PolarAisleRadialWidth = p.AisleRadialWidthDegrees > 0 ? p.AisleRadialWidthDegrees : 5;
        PolarAisleCircularRings = p.AisleCircularAfterRings is { Count: > 0 } ? string.Join("," , p.AisleCircularAfterRings) : "";
        PolarAisleCircularWidth = p.AisleCircularWidth > 0 ? p.AisleCircularWidth : 20;
        PolarFrontRowCount = p.FrontRowCount > 0 ? p.FrontRowCount : 1;
        PolarEmptyPositionsSpec = p.EmptyPositions is { Count: > 0 }
            ? string.Join(";" , p.EmptyPositions.Select(e => $"{e.Ring},{e.AngleDegrees:F2}"))
            : "";
    }

    private void ResetParameters ()
    {
        GridRows = 5; GridColumns = 8;
        GridHorizontalSpacing = 64; GridVerticalSpacing = 56;
        GridOriginX = 200; GridOriginY = 200;
        GridSeatsPerDesk = 2;
        GridIntraDeskSpacing = 40; GridInterDeskSpacing = 64;
        GridAisleAfterColumns = ""; GridAisleAfterRows = "";
        GridAisleWidth = 60;
        GridFrontRowCount = 1;
        GridHasPodium = true; GridPodiumWidth = 100; GridPodiumHeight = 40;
        GridColumnRowCountsSpec = ""; GridEmptyPositionsSpec = "";
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
    }

    private static List<int> ParseIntList (string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return csv.Split(',' , StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim() , out var n) ? n : -1)
            .Where(n => n > 0)
            .ToList();
    }

    private static List<double> ParseDoubleList (string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return csv.Split(',' , StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.TryParse(s.Trim() , out var n) ? n : -1)
            .Where(n => n >= 0)
            .ToList();
    }

    private static List<GridPosition> ParseGridEmptyPositions (string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return [];
        return spec.Split(';' , StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var parts = part.Split(',');
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim() , out var row)
                    && int.TryParse(parts[1].Trim() , out var col)
                    && row > 0 && col > 0)
                    return new GridPosition { Row = row , Column = col };
                return null;
            })
            .Where(p => p != null)
            .Cast<GridPosition>()
            .ToList();
    }

    private static List<PolarRingAngle> ParsePolarEmptyPositions (string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return [];
        return spec.Split(';' , StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var parts = part.Split(',');
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim() , out var ring)
                    && double.TryParse(parts[1].Trim() , out var angle)
                    && ring > 0)
                    return new PolarRingAngle { Ring = ring , AngleDegrees = angle };
                return null;
            })
            .Where(p => p != null)
            .Cast<PolarRingAngle>()
            .ToList();
    }

    partial void OnSelectedVenueItemChanged (VenueItem? value)
    {
        if (!_suppressAutoLoad && value != null)
        {
            _selectVenueCts?.Cancel();
            _selectVenueCts = new CancellationTokenSource();
            _ = SelectVenueAsync(value , _selectVenueCts.Token);
        }
    }

    partial void OnGridSeatsPerDeskChanged (int value) => RegenerateAisleOptions();
    partial void OnGridColumnsChanged (int value) => RegenerateAisleOptions();
    partial void OnGridRowsChanged (int value) => RegenerateAisleOptions();
}

public record VenueItem (string Id , string Name);

public partial class DoorItem : ObservableObject
{
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private string _label = "门";

    public DoorItem () { }
    public DoorItem (double x , double y , string label = "门")
    {
        _x = x; _y = y; _label = label;
    }
}

public partial class AisleOption : ObservableObject
{
    public string Label { get; set; } = "";
    public int SeatColumn { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    public AisleOption (string label , int seatColumn , bool selected = false)
    {
        Label = label;
        SeatColumn = seatColumn;
        _isSelected = selected;
    }
}

public class SeatPreview
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Label { get; set; } = string.Empty;
    public PreviewElementType ElementType { get; set; } = PreviewElementType.Seat;
    public double Width { get; set; } = 20;
    public double Height { get; set; } = 20;
    public bool IsFrontRow { get; set; }
    public global::Avalonia.CornerRadius CornerRadius { get; set; } = new(2);
    public double Rotation { get; set; }
    public string BackgroundColor { get; set; } = "#800072C6";
    public string BorderColor { get; set; } = "";
    public global::Avalonia.Thickness BorderThickness { get; set; }
    public bool IsCircle { get; set; }
    public string? PathData { get; set; }
    public string PathFill { get; set; } = "";
    public global::Avalonia.Media.StreamGeometry? PathGeometry { get; set; }
}

public enum PreviewElementType
{
    Seat,
    Obstacle,
    Podium,
    Door,
    Aisle
}
