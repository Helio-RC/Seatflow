using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    [ObservableProperty] private double _gridOriginX = 0;
    [ObservableProperty] private double _gridOriginY = 0;

    // ── Grid 桌面配置 ──
    [ObservableProperty] private int _gridSeatsPerDesk = 2;
    [ObservableProperty] private double _gridIntraDeskSpacing = 12;
    [ObservableProperty] private double _gridInterDeskSpacing = 40;

    // ── Grid 过道配置 ──
    [ObservableProperty] private string _gridAisleAfterColumns = "";
    [ObservableProperty] private string _gridAisleAfterRows = "";
    [ObservableProperty] private double _gridAisleWidth = 60;

    // ── Grid 教室特征 ──
    [ObservableProperty] private int _gridFrontRowCount = 1;
    [ObservableProperty] private bool _gridHasPodium = true;
    [ObservableProperty] private double _gridPodiumWidth = 60;
    [ObservableProperty] private double _gridPodiumHeight = 40;
    [ObservableProperty] private bool _gridHasFrontDoor = false;

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

        // 前门
        if (meta.HasFrontDoor)
        {
            double doorX = meta.OriginX - 30;
            double doorY = meta.OriginY;
            overlays.Add(new SeatPreview
            {
                X = doorX, Y = doorY,
                Width = 20, Height = 30,
                ElementType = PreviewElementType.Door,
                Label = "前门"
            });
        }

        PreviewOverlays = new ObservableCollection<SeatPreview>(overlays);
        StatusMessage = $"预览：{seats.Count} 个座位";
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
                if (meta.HasFrontDoor)
                {
                    layout.Obstacles.Add(new Obstacle
                    {
                        X = meta.OriginX - 30,
                        Y = meta.OriginY,
                        Width = 20, Height = 30,
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
            PodiumHeight = GridPodiumHeight,
            HasFrontDoor = GridHasFrontDoor
        };
    }

    private void PopulateGridFromMetadata(GridLayoutMetadata g)
    {
        GridRows = g.Rows > 0 ? g.Rows : 5;
        GridColumns = g.Columns > 0 ? g.Columns : 8;
        GridHorizontalSpacing = g.HorizontalSpacing > 0 ? g.HorizontalSpacing : 40;
        GridVerticalSpacing = g.VerticalSpacing > 0 ? g.VerticalSpacing : 36;
        GridOriginX = g.OriginX;
        GridOriginY = g.OriginY;
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
        GridHasFrontDoor = g.HasFrontDoor;
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
        GridOriginX = 0; GridOriginY = 0;
        GridSeatsPerDesk = 2;
        GridIntraDeskSpacing = 12; GridInterDeskSpacing = 40;
        GridAisleAfterColumns = ""; GridAisleAfterRows = "";
        GridAisleWidth = 60;
        GridFrontRowCount = 1;
        GridHasPodium = true; GridPodiumWidth = 60; GridPodiumHeight = 40;
        GridHasFrontDoor = false;
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
}

public record VenueItem(string Id, string Name);

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
