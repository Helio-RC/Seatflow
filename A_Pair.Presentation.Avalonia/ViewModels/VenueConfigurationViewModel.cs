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

    public VenueConfigurationViewModel(IApplicationFacade facade, IDialogService dialog, INavigationService navigation)
    {
        _facade = facade;
        _navigation = navigation;
        _ = LoadVenueList();
    }

    [RelayCommand]
    private void NavigateToFreeform()
    {
        _navigation.NavigateTo(PageKey.FreeformManagement);
    }

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
            if (layout == null)
            {
                StatusMessage = $"加载会场「{item.Name}」失败";
                return;
            }

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
                break;
        }

        PreviewSeats = new ObservableCollection<SeatPreview>(seats);
        StatusMessage = $"预览：{seats.Count} 个座位";
    }

    private ClassroomLayoutDefinition BuildLayoutDefinition()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Id = SelectedVenueItem?.Id ?? "",
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

    private void ResetParameters()
    {
        GridRows = 5; GridColumns = 6;
        GridHorizontalSpacing = 32; GridVerticalSpacing = 32;
        GridOriginX = 0; GridOriginY = 0;
        PolarRings = 3; PolarSeatsPerRing = 12;
        PolarRadiusStep = 40; PolarStartAngle = 0;
        PolarOriginX = 0; PolarOriginY = 0;
    }

    partial void OnSelectedVenueItemChanged(VenueItem? value)
    {
        if (!_suppressAutoLoad && value != null)
            _ = SelectVenueAsync(value);
    }

    partial void OnSelectedLayoutTypeChanged(LayoutType value) => RegeneratePreview();
}

public record VenueItem(string Id, string Name);

public class SeatPreview
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Label { get; set; } = string.Empty;
}
