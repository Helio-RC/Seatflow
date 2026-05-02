using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Presentation.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class VenueConfigurationViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;

    public string Title { get; } = "会场配置";

    [ObservableProperty]
    private ObservableCollection<string> _venueIds = [];

    private bool _suppressAutoLoad;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVenue))]
    private string? _selectedVenueId;

    [ObservableProperty]
    private string _layoutName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridSelected))]
    [NotifyPropertyChangedFor(nameof(IsPolarSelected))]
    [NotifyPropertyChangedFor(nameof(IsFreeformSelected))]
    private LayoutType _selectedLayoutType = LayoutType.Grid;

    public bool HasSelectedVenue => SelectedVenueId != null;

    public bool IsGridSelected => SelectedLayoutType == LayoutType.Grid;
    public bool IsPolarSelected => SelectedLayoutType == LayoutType.Polar;
    public bool IsFreeformSelected => SelectedLayoutType == LayoutType.Freeform;

    // Grid 参数
    [ObservableProperty] private int _gridRows = 5;
    [ObservableProperty] private int _gridColumns = 6;
    [ObservableProperty] private double _gridHorizontalSpacing = 32;
    [ObservableProperty] private double _gridVerticalSpacing = 32;
    [ObservableProperty] private double _gridOriginX = 0;
    [ObservableProperty] private double _gridOriginY = 0;

    // Polar 参数
    [ObservableProperty] private int _polarRings = 3;
    [ObservableProperty] private int _polarSeatsPerRing = 12;
    [ObservableProperty] private double _polarRadiusStep = 40;
    [ObservableProperty] private double _polarStartAngle = 0;
    [ObservableProperty] private double _polarOriginX = 0;
    [ObservableProperty] private double _polarOriginY = 0;

    // 预览
    [ObservableProperty] private ObservableCollection<SeatPreview> _previewSeats = [];
    [ObservableProperty] private string _statusMessage = string.Empty;

    public VenueConfigurationViewModel(IApplicationFacade facade, IDialogService dialog)
    {
        _facade = facade;
        _ = LoadVenueList(); // 启动后自动加载会场列表
    }

    [RelayCommand]
    private async Task LoadVenueList()
    {
        await SafeExecuteAsync(async () =>
        {
            var ids = await _facade.ListVenueIdsAsync();
            VenueIds = new ObservableCollection<string>(ids);
            StatusMessage = $"已加载 {ids.Count()} 个会场";
        });
    }

    [RelayCommand]
    private void NewVenue()
    {
        _suppressAutoLoad = true;
        var id = Guid.NewGuid().ToString("N")[..8];
        LayoutName = $"新会场_{id}";
        SelectedLayoutType = LayoutType.Grid;
        ResetParameters();
        SelectedVenueId = id;
        RegeneratePreview();
        StatusMessage = "已创建新会场，请编辑参数后保存";
        _suppressAutoLoad = false;
    }

    [RelayCommand]
    private async Task SelectVenue(string venueId)
    {
        await SafeExecuteAsync(async () =>
        {
            var layout = await _facade.LoadVenueAsync(venueId);
            if (layout == null)
            {
                StatusMessage = $"加载会场 '{venueId}' 失败";
                return;
            }

            SelectedVenueId = venueId;
            LayoutName = layout.Name;
            SelectedLayoutType = layout.LayoutType;

            switch (layout.Metadata)
            {
                case GridLayoutMetadata g:
                    GridRows = g.Rows > 0 ? g.Rows : 5;
                    GridColumns = g.Columns > 0 ? g.Columns : 6;
                    GridHorizontalSpacing = g.HorizontalSpacing > 0 ? g.HorizontalSpacing : 32;
                    GridVerticalSpacing = g.VerticalSpacing > 0 ? g.VerticalSpacing : 32;
                    GridOriginX = g.OriginX;
                    GridOriginY = g.OriginY;
                    break;
                case PolarLayoutMetadata p:
                    PolarRings = p.Rings > 0 ? p.Rings : 3;
                    PolarSeatsPerRing = p.SeatsPerRing > 0 ? p.SeatsPerRing : 12;
                    PolarRadiusStep = p.RadiusStep > 0 ? p.RadiusStep : 40;
                    PolarStartAngle = p.StartAngleDegrees;
                    PolarOriginX = p.OriginX;
                    PolarOriginY = p.OriginY;
                    break;
            }

            RegeneratePreview();
            StatusMessage = $"已加载会场 '{layout.Name}'，共 {layout.Seats.Count} 个座位";
        });
    }

    [RelayCommand]
    private async Task SaveVenue()
    {
        if (string.IsNullOrWhiteSpace(SelectedVenueId)) return;

        await SafeExecuteAsync(async () =>
        {
            var layout = BuildLayoutDefinition();
            await _facade.SaveVenueAsync(SelectedVenueId, layout);
            await RefreshVenueList();
            StatusMessage = $"会场 '{layout.Name}' 已保存，共 {layout.Seats.Count} 个座位";
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
    private void RegeneratePreview()
    {
        var seats = new List<SeatPreview>();
        switch (SelectedLayoutType)
        {
            case LayoutType.Grid:
                for (int r = 0; r < GridRows; r++)
                    for (int c = 0; c < GridColumns; c++)
                        seats.Add(new SeatPreview
                        {
                            X = GridOriginX + c * GridHorizontalSpacing,
                            Y = GridOriginY + r * GridVerticalSpacing,
                            Label = $"R{r + 1}C{c + 1}"
                        });
                break;

            case LayoutType.Polar:
                for (int ring = 0; ring < PolarRings; ring++)
                {
                    double radius = (ring + 1) * PolarRadiusStep;
                    for (int i = 0; i < PolarSeatsPerRing; i++)
                    {
                        double angleDeg = PolarStartAngle + (360.0 / PolarSeatsPerRing) * i;
                        double angleRad = angleDeg * Math.PI / 180.0;
                        seats.Add(new SeatPreview
                        {
                            X = PolarOriginX + radius * Math.Cos(angleRad),
                            Y = PolarOriginY + radius * Math.Sin(angleRad),
                            Label = $"R{ring + 1}S{i + 1}"
                        });
                    }
                }
                break;

            case LayoutType.Freeform:
                // Freeform 无预览生成
                break;
        }

        PreviewSeats = new ObservableCollection<SeatPreview>(seats);
        StatusMessage = $"预览：{seats.Count} 个座位";
    }

    private ClassroomLayoutDefinition BuildLayoutDefinition()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Id = SelectedVenueId ?? "",
            Name = LayoutName,
            LayoutType = SelectedLayoutType,
        };

        switch (SelectedLayoutType)
        {
            case LayoutType.Grid:
                layout.Metadata = new GridLayoutMetadata
                {
                    Rows = GridRows,
                    Columns = GridColumns,
                    HorizontalSpacing = GridHorizontalSpacing,
                    VerticalSpacing = GridVerticalSpacing,
                    OriginX = GridOriginX,
                    OriginY = GridOriginY
                };
                for (int r = 1; r <= GridRows; r++)
                    for (int c = 1; c <= GridColumns; c++)
                        layout.Seats.Add(new GridSeat { Row = r, Column = c });
                break;

            case LayoutType.Polar:
                layout.Metadata = new PolarLayoutMetadata
                {
                    Rings = PolarRings,
                    SeatsPerRing = PolarSeatsPerRing,
                    RadiusStep = PolarRadiusStep,
                    StartAngleDegrees = PolarStartAngle,
                    OriginX = PolarOriginX,
                    OriginY = PolarOriginY
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
                layout.Metadata = new FreeformLayoutMetadata();
                break;
        }

        return layout;
    }

    private async Task RefreshVenueList()
    {
        var ids = await _facade.ListVenueIdsAsync();
        VenueIds = new ObservableCollection<string>(ids);
    }

    private void ResetParameters()
    {
        GridRows = 5; GridColumns = 6;
        GridHorizontalSpacing = 32; GridVerticalSpacing = 32;
        GridOriginX = 0; GridOriginY = 0;
        PolarRings = 3; PolarSeatsPerRing = 12;
        PolarRadiusStep = 40; PolarStartAngle = 0;
        PolarOriginX = 0; PolarOriginY = 0;
    }

    partial void OnSelectedVenueIdChanged(string? value)
    {
        if (!_suppressAutoLoad && value != null)
            _ = SelectVenue(value);
    }

    partial void OnSelectedLayoutTypeChanged(LayoutType value) => RegeneratePreview();
    partial void OnGridRowsChanged(int value) { if (IsGridSelected) RegeneratePreview(); }
    partial void OnGridColumnsChanged(int value) { if (IsGridSelected) RegeneratePreview(); }
    partial void OnGridHorizontalSpacingChanged(double value) { if (IsGridSelected) RegeneratePreview(); }
    partial void OnGridVerticalSpacingChanged(double value) { if (IsGridSelected) RegeneratePreview(); }
    partial void OnGridOriginXChanged(double value) { if (IsGridSelected) RegeneratePreview(); }
    partial void OnGridOriginYChanged(double value) { if (IsGridSelected) RegeneratePreview(); }
    partial void OnPolarRingsChanged(int value) { if (IsPolarSelected) RegeneratePreview(); }
    partial void OnPolarSeatsPerRingChanged(int value) { if (IsPolarSelected) RegeneratePreview(); }
    partial void OnPolarRadiusStepChanged(double value) { if (IsPolarSelected) RegeneratePreview(); }
    partial void OnPolarStartAngleChanged(double value) { if (IsPolarSelected) RegeneratePreview(); }
    partial void OnPolarOriginXChanged(double value) { if (IsPolarSelected) RegeneratePreview(); }
    partial void OnPolarOriginYChanged(double value) { if (IsPolarSelected) RegeneratePreview(); }
}

/// <summary>
/// 预览座位的简化模型，包含画布坐标和标签。
/// </summary>
public class SeatPreview
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Label { get; set; } = string.Empty;
}
