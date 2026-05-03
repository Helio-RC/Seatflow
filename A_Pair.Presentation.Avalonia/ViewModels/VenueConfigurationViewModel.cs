using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
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
    [ObservableProperty] private double _gridPodiumWidth = 60;
    [ObservableProperty] private double _gridPodiumHeight = 40;

    // ── 门配置（支持多门、自定义位置）──
    [ObservableProperty] private ObservableCollection<DoorItem> _doorItems = [];

    // ── Polar 参数 ──
    [ObservableProperty] private int _polarRings = 3;
    [ObservableProperty] private int _polarSeatsPerRing = 12;
    [ObservableProperty] private double _polarRadiusStep = 40;
    [ObservableProperty] private double _polarStartAngle = 0;
    [ObservableProperty] private double _polarOriginX = 0;
    [ObservableProperty] private double _polarOriginY = 0;

    // ── 预览 ──
    [ObservableProperty] private ObservableCollection<SeatPreview> _previewSeats = [];
    [ObservableProperty] private ObservableCollection<SeatPreview> _previewOverlays = [];
    [ObservableProperty] private string _statusMessage = string.Empty;

    public VenueConfigurationViewModel(IApplicationFacade facade, IDialogService dialog, INavigationService navigation)
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
    private async Task LoadVenueList()
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
            StatusMessage = $"已加载 {items.Count} 个会场";
        });
    }

    [RelayCommand]
    private void NewVenue()
    {
        _suppressAutoLoad = true;
        var id = Guid.NewGuid().ToString("N")[..8];
        var item = new VenueItem(id, $"新会场_{id}");
        LayoutName = item.Name;
        SelectedLayoutType = LayoutType.Grid;
        ResetParameters();
        VenueItems.Add(item);
        SelectedVenueItem = item;
        RegeneratePreview();
        StatusMessage = "已创建新会场，请编辑参数后保存";
        _suppressAutoLoad = false;
    }

    [RelayCommand]
    private async Task DeleteVenue()
    {
        if (SelectedVenueItem == null) return;
        var item = SelectedVenueItem;
        var confirmed = await Dialog.ShowConfirmAsync("确认删除", $"确定要删除会场「{item.Name}」吗？此操作不可恢复。");
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
        }, "删除会场失败");
    }

    private async Task SelectVenueAsync(VenueItem item)
    {
        await SafeExecuteAsync(async () =>
        {
            var layout = await _facade.LoadVenueAsync(item.Id);
            if (layout == null) { StatusMessage = $"加载会场「{item.Name}」失败"; return; }

            LayoutName = layout.Name;
            SelectedLayoutType = layout.LayoutType;

            switch (layout.Metadata)
            {
                case GridLayoutMetadata g:
                    PopulateGridFromMetadata(g);
                    break;
                case PolarLayoutMetadata p:
                    PopulatePolarFromMetadata(p);
                    break;
            }

            RegeneratePreview();
            StatusMessage = $"已加载会场「{layout.Name}」，共 {layout.Seats.Count} 个座位";
        });
    }

    [RelayCommand]
    private async Task SaveVenue()
    {
        if (SelectedVenueItem == null) return;
        var item = SelectedVenueItem;

        await SafeExecuteAsync(async () =>
        {
            var layout = BuildLayoutDefinition();
            await _facade.SaveVenueAsync(item.Id, layout);
            await LoadVenueList();
            SelectedVenueItem = VenueItems.FirstOrDefault(v => v.Id == item.Id);
            StatusMessage = $"会场「{layout.Name}」已保存，共 {layout.Seats.Count} 个座位";
        }, "保存会场失败");
    }

    [RelayCommand]
    private void SelectLayoutType(string type)
    {
        SelectedLayoutType = type switch
        {
            "Polar" => LayoutType.Polar,
            "Freeform" => LayoutType.Freeform,
            _ => LayoutType.Grid,
        };
    }

    [RelayCommand]
    private void AddDoor()
    {
        double dx = GridOriginX - 50;
        double dy = GridOriginY - 20;
        var door = new DoorItem(dx, dy, $"门 {DoorItems.Count + 1}");
        door.RemoveSelf = () => DoorItems.Remove(door);
        DoorItems.Add(door);
    }

    [RelayCommand]
    private void NavigateToFreeform() => _navigation.NavigateTo(PageKey.FreeformManagement);

    // ═══════════════════════════════════════════════
    // 预览 & 构建
    // ═══════════════════════════════════════════════

    [RelayCommand]
    private void RegeneratePreview()
    {
        if (SelectedLayoutType != LayoutType.Grid) return;

        var meta = BuildGridMetadata();
        var layout = GridLayoutBuilder.BuildGrid(meta);

        // 座位
        var seats = new List<SeatPreview>();
        foreach (GridSeat s in layout.Seats.Cast<GridSeat>())
        {
            var (x, y) = SeatGeometryHelper.GetPosition(s, meta);
            bool isFront = s.Row <= meta.FrontRowCount;
            int deskNum = (s.Column - 1) / meta.SeatsPerDesk + 1;
            seats.Add(new SeatPreview
            {
                X = x, Y = y,
                Label = $"R{s.Row}C{s.Column} (桌{deskNum})",
                ElementType = PreviewElementType.Seat,
                IsFrontRow = isFront
            });
        }
        PreviewSeats = new ObservableCollection<SeatPreview>(seats);

        // 覆盖层
        var overlays = new List<SeatPreview>();

        // 讲台
        if (meta.HasPodium && meta.PodiumWidth > 0 && meta.PodiumHeight > 0)
        {
            double podiumX = meta.OriginX;
            double podiumY = meta.OriginY - meta.PodiumHeight - meta.VerticalSpacing;
            overlays.Add(new SeatPreview
            {
                X = podiumX, Y = podiumY,
                Width = meta.PodiumWidth * meta.Columns / 2,
                Height = meta.PodiumHeight,
                ElementType = PreviewElementType.Podium,
                Label = "讲台"
            });
        }

        // 门（从 DoorItems 集合）
        foreach (var door in DoorItems)
        {
            overlays.Add(new SeatPreview
            {
                X = door.X, Y = door.Y,
                Width = 36, Height = 24,
                ElementType = PreviewElementType.Door,
                Label = door.Label
            });
        }

        PreviewOverlays = new ObservableCollection<SeatPreview>(overlays);
        StatusMessage = $"预览：{seats.Count} 个座位";
    }

    private void RegenerateAisleOptions()
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
            int leftStart = (d - 1) * spd + 1;
            int leftEnd = d * spd;
            int rightStart = d * spd + 1;
            int rightEnd = Math.Min((d + 1) * spd, GridColumns);
            string label = $"{leftStart}-{leftEnd} 列 ↔ {rightStart}-{rightEnd} 列";
            colOptions.Add(new AisleOption(label, seatCol, prevCols.Contains(seatCol)));
        }
        AisleColumnOptions = new ObservableCollection<AisleOption>(colOptions);
        foreach (var opt in AisleColumnOptions)
            opt.PropertyChanged += (_, _) => { if (opt.IsSelected != prevCols.Contains(opt.SeatColumn)) SyncAisleColumnsFromOptions(); };

        // 行过道选项
        var rowOptions = new List<AisleOption>();
        for (int r = 1; r < GridRows; r++)
        {
            string label = $"第 {r} 排 ↔ 第 {r + 1} 排";
            rowOptions.Add(new AisleOption(label, r, prevRows.Contains(r)));
        }
        AisleRowOptions = new ObservableCollection<AisleOption>(rowOptions);
        foreach (var opt in AisleRowOptions)
            opt.PropertyChanged += (_, _) => { if (opt.IsSelected != prevRows.Contains(opt.SeatColumn)) SyncAisleRowsFromOptions(); };
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

    private ClassroomLayoutDefinition BuildLayoutDefinition()
    {
        ClassroomLayoutDefinition layout;
        switch (SelectedLayoutType)
        {
            case LayoutType.Grid:
                var meta = BuildGridMetadata();
                layout = GridLayoutBuilder.BuildGrid(meta);
                // 将讲台/前门作为 Obstacle 写入
                if (meta.HasPodium && meta.PodiumWidth > 0 && meta.PodiumHeight > 0)
                {
                    layout.Obstacles.Add(new Obstacle
                    {
                        X = meta.OriginX,
                        Y = meta.OriginY - meta.PodiumHeight - meta.VerticalSpacing,
                        Width = meta.PodiumWidth,
                        Height = meta.PodiumHeight,
                        Type = "Podium"
                    });
                }
                foreach (var door in DoorItems)
                {
                    layout.Obstacles.Add(new Obstacle
                    {
                        X = door.X, Y = door.Y,
                        Width = 36, Height = 24,
                        Type = "Door"
                    });
                }
                break;

            case LayoutType.Polar:
                layout = new ClassroomLayoutDefinition
                {
                    Id = SelectedVenueItem?.Id ?? "",
                    Name = LayoutName,
                    LayoutType = LayoutType.Polar,
                    Metadata = new PolarLayoutMetadata
                    {
                        Rings = PolarRings,
                        SeatsPerRing = PolarSeatsPerRing,
                        RadiusStep = PolarRadiusStep,
                        StartAngleDegrees = PolarStartAngle,
                        OriginX = PolarOriginX,
                        OriginY = PolarOriginY
                    }
                };
                for (int ring = 1; ring <= PolarRings; ring++)
                {
                    double radius = ring * PolarRadiusStep;
                    for (int i = 0; i < PolarSeatsPerRing; i++)
                    {
                        double angleDeg = PolarStartAngle + (360.0 / PolarSeatsPerRing) * i;
                        layout.Seats.Add(new PolarSeat { Radius = radius, AngleDegrees = angleDeg });
                    }
                }
                break;

            case LayoutType.Freeform:
                layout = new ClassroomLayoutDefinition
                {
                    Id = SelectedVenueItem?.Id ?? "",
                    Name = LayoutName,
                    LayoutType = LayoutType.Freeform,
                    Metadata = new FreeformLayoutMetadata()
                };
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

    private GridLayoutMetadata BuildGridMetadata()
    {
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
            PodiumHeight = GridPodiumHeight
        };
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
        GridInterDeskSpacing = g.InterDeskSpacing > 0 ? g.InterDeskSpacing : 40;
        GridAisleAfterColumns = string.Join(",", g.AisleAfterColumns ?? []);
        GridAisleAfterRows = string.Join(",", g.AisleAfterRows ?? []);
        GridAisleWidth = g.AisleWidth > 0 ? g.AisleWidth : 60;
        GridFrontRowCount = g.FrontRowCount > 0 ? g.FrontRowCount : 1;
        GridHasPodium = g.HasPodium;
        GridPodiumWidth = g.PodiumWidth > 0 ? g.PodiumWidth : 60;
        GridPodiumHeight = g.PodiumHeight > 0 ? g.PodiumHeight : 40;
    }

    private void PopulatePolarFromMetadata(PolarLayoutMetadata p)
    {
        PolarRings = p.Rings > 0 ? p.Rings : 3;
        PolarSeatsPerRing = p.SeatsPerRing > 0 ? p.SeatsPerRing : 12;
        PolarRadiusStep = p.RadiusStep > 0 ? p.RadiusStep : 40;
        PolarStartAngle = p.StartAngleDegrees;
        PolarOriginX = p.OriginX;
        PolarOriginY = p.OriginY;
    }

    private void ResetParameters()
    {
        GridRows = 5; GridColumns = 8;
        GridHorizontalSpacing = 40; GridVerticalSpacing = 36;
        GridOriginX = 200; GridOriginY = 200;
        GridSeatsPerDesk = 2;
        GridIntraDeskSpacing = 12; GridInterDeskSpacing = 40;
        GridAisleAfterColumns = ""; GridAisleAfterRows = "";
        GridAisleWidth = 60;
        GridFrontRowCount = 1;
        GridHasPodium = true; GridPodiumWidth = 60; GridPodiumHeight = 40;
        DoorItems.Clear();
        PolarRings = 3; PolarSeatsPerRing = 12;
        PolarRadiusStep = 40; PolarStartAngle = 0;
        PolarOriginX = 0; PolarOriginY = 0;
    }

    private static List<int> ParseIntList(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var n) ? n : -1)
            .Where(n => n > 0)
            .ToList();
    }

    partial void OnSelectedVenueItemChanged(VenueItem? value)
    {
        if (!_suppressAutoLoad && value != null)
            _ = SelectVenueAsync(value);
    }

    partial void OnGridSeatsPerDeskChanged(int value) => RegenerateAisleOptions();
    partial void OnGridColumnsChanged(int value) => RegenerateAisleOptions();
    partial void OnGridRowsChanged(int value) => RegenerateAisleOptions();
}

public record VenueItem(string Id, string Name);

public class DoorItem : ObservableObject
{
    private double _x;
    public double X { get => _x; set => SetProperty(ref _x, value); }

    private double _y;
    public double Y { get => _y; set => SetProperty(ref _y, value); }

    private string _label = "门";
    public string Label { get => _label; set => SetProperty(ref _label, value); }

    public Action? RemoveSelf { get; set; }
    public ICommand RemoveCommand { get; }

    public DoorItem()
    {
        RemoveCommand = new RelayCommand(() => RemoveSelf?.Invoke());
    }

    public DoorItem(double x, double y, string label = "门", Action? removeSelf = null) : this()
    {
        X = x; Y = y; Label = label;
        RemoveSelf = removeSelf;
    }
}

public partial class AisleOption : ObservableObject
{
    public string Label { get; set; } = "";
    public int SeatColumn { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    public AisleOption(string label, int seatColumn, bool selected = false)
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
}

public enum PreviewElementType
{
    Seat,
    Obstacle,
    Podium,
    Door,
    Aisle
}
