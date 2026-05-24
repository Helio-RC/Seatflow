using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.DomainServices;
using A_Pair.Core.Models;
using A_Pair.Presentation.Avalonia.Services;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class SnapshotHistoryViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly INavigationService _navigation;
    private readonly ILogger<SnapshotHistoryViewModel> _logger;

    public string Title { get; } = "历史快照";

    [ObservableProperty]
    private ObservableCollection<VenueItem> _venues = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVenue))]
    private VenueItem? _selectedVenue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSnapshots))]
    [NotifyPropertyChangedFor(nameof(CanEnterBatchDelete))]
    private ObservableCollection<SeatingSnapshot> _snapshots = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSnapshot))]
    private SeatingSnapshot? _selectedSnapshot;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "请选择会场查看快照";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBatchMode))]
    [NotifyPropertyChangedFor(nameof(CanEnterBatchDelete))]
    private bool _isBatchDeleteMode;

    public bool IsNotBatchMode => !IsBatchDeleteMode;

    [ObservableProperty]
    private ObservableCollection<SelectableItem> _checkableItems = [];

    [ObservableProperty]
    private bool _isAllSelected;

    // ── 预览 ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private ObservableCollection<SeatDisplayItem> _previewSeats = [];

    [ObservableProperty]
    private ObservableCollection<SeatDisplayItem> _previewOverlays = [];

    public bool HasPreview => PreviewSeats.Count > 0;

    [ObservableProperty]
    private double _previewCanvasWidth;

    [ObservableProperty]
    private double _previewCanvasHeight;

    public bool HasSelectedVenue => SelectedVenue != null;
    public bool HasSelectedSnapshot => SelectedSnapshot != null;
    public bool HasSnapshots => Snapshots.Count > 0;
    public bool CanCreateSnapshot => _facade.HasActiveWorkspace;
    public bool CanEnterBatchDelete => HasSnapshots && !IsBatchDeleteMode;

    public SnapshotHistoryViewModel (IApplicationFacade facade , INavigationService navigation , ILogger<SnapshotHistoryViewModel>? logger = null)
    {
        _facade = facade;
        _navigation = navigation;
        _logger = logger ?? NullLogger<SnapshotHistoryViewModel>.Instance;
        _ = LoadVenuesAsync();
    }

    [RelayCommand]
    private async Task LoadVenuesAsync ()
    {
        await SafeExecuteAsync(async () =>
        {
            var ids = (await _facade.ListVenueIdsAsync()).ToList();
            var items = new ObservableCollection<VenueItem>();
            foreach (var id in ids)
            {
                var layout = await _facade.LoadVenueAsync(id);
                if (layout != null)
                    items.Add(new VenueItem(id , layout.Name));
            }
            Venues = items;
            StatusMessage = $"已加载 {items.Count} 个会场";
        });
    }

    [RelayCommand]
    private async Task CreateSnapshotAsync ()
    {
        var (confirmed , description) = await Dialog.ShowInputAsync(
            "创建快照" , "请输入快照描述：" , $"手动快照 - {DateTime.Now:yyyy-MM-dd HH:mm}");
        if (!confirmed || string.IsNullOrWhiteSpace(description)) return;

        await SafeExecuteAsync(async () =>
        {
            var snapshot = await _facade.CreateSnapshotAsync(description);
            if (snapshot == null)
            {
                await Dialog.ShowWarningAsync("创建快照失败" , "当前没有活动的工作区，请先生成座位安排。");
                return;
            }
            if (SelectedVenue != null)
                await LoadSnapshotsAsync();
            StatusMessage = "快照已创建";
        } , "创建快照失败");
    }

    partial void OnSelectedVenueChanged (VenueItem? value)
    {
        if (value != null)
            _ = LoadSnapshotsAsync();
    }

    partial void OnSelectedSnapshotChanged (SeatingSnapshot? value)
    {
        if (value != null)
            _ = BuildPreviewAsync(value);
        else
        {
            PreviewSeats = [];
            PreviewOverlays = [];
            PreviewCanvasWidth = 0;
            PreviewCanvasHeight = 0;
        }
    }

    private async Task BuildPreviewAsync (SeatingSnapshot snapshot)
    {
        var seats = new ObservableCollection<SeatDisplayItem>();
        var overlays = new ObservableCollection<SeatDisplayItem>();
        try
        {
            var layout = await _facade.LoadVenueAsync(snapshot.LayoutId);
            if (layout == null || layout.Seats.Count == 0)
            {
                PreviewSeats = seats;
                PreviewOverlays = overlays;
                return;
            }

            var metadata = layout.Metadata!;
            var assignments = snapshot.SeatAssignments;
            var (baseW , baseH) = ComputeSeatSize(metadata);

            // 第一遍：收集原始坐标
            double minX = double.MaxValue, minY = double.MaxValue, maxX = 0, maxY = 0;
            var raw = new List<(double cx , double cy , Seat seat)>();
            foreach (var seat in layout.Seats)
            {
                if (!seat.IsAvailable) continue;
                var pos = SeatGeometryHelper.GetPosition(seat , metadata);
                raw.Add((pos.X , pos.Y , seat));
                var (cx , cy) = pos;
                minX = Math.Min(minX , cx);
                minY = Math.Min(minY , cy);
                maxX = Math.Max(maxX , cx + baseW);
                maxY = Math.Max(maxY , cy + baseH);
            }

            double canvasW = Math.Max(maxX - minX + 40 , 200);
            double canvasH = Math.Max(maxY - minY + 40 , 150);
            double offsetX = 20 - minX;
            double offsetY = 20 - minY;
            double scale = 0.55; // 缩略图缩放

            foreach (var (cx , cy , seat) in raw)
            {
                bool occupied = assignments.TryGetValue(seat.Id , out var sid) && !string.IsNullOrEmpty(sid);
                seats.Add(new SeatDisplayItem
                {
                    X = (cx + offsetX) * scale ,
                    Y = (cy + offsetY) * scale ,
                    Width = baseW * scale ,
                    Height = baseH * scale ,
                    SeatId = seat.Id ,
                    SeatLabel = BuildSeatLabel(seat) ,
                    IsOccupied = occupied ,
                    StudentId = occupied ? sid : null ,
                    StudentName = occupied ? sid : null ,
                    OccupancyStatus = occupied ? SeatOccupancyStatus.Occupied : SeatOccupancyStatus.Empty ,
                    CornerRadius = new CornerRadius(2)
                });
            }

            // 障碍物
            foreach (var obs in layout.Obstacles)
            {
                overlays.Add(new SeatDisplayItem
                {
                    X = (obs.X + offsetX) * scale ,
                    Y = (obs.Y + offsetY) * scale ,
                    Width = (obs.Width > 0 ? obs.Width : 40) * scale ,
                    Height = (obs.Height > 0 ? obs.Height : 30) * scale ,
                    SeatLabel = obs.Type ,
                    IsOccupied = true ,
                    OccupancyStatus = SeatOccupancyStatus.Fixed
                });
            }

            PreviewCanvasWidth = canvasW * scale;
            PreviewCanvasHeight = canvasH * scale;
        }
        catch
        {
            // 预览失败不阻塞 UI
        }

        PreviewSeats = seats;
        PreviewOverlays = overlays;
    }

    private static (double W , double H) ComputeSeatSize (LayoutMetadata metadata)
    {
        return metadata switch
        {
            GridLayoutMetadata g => (Math.Clamp(g.HorizontalSpacing * 0.8 , 44 , 72) , Math.Clamp(g.VerticalSpacing * 0.55 , 24 , 44)) ,
            PolarLayoutMetadata p => (Math.Clamp(p.RadiusStep * 0.75 , 28 , 48) , Math.Clamp(p.RadiusStep * 0.75 , 28 , 48)) ,
            _ => (42 , 26)
        };
    }

    private static string BuildSeatLabel (Seat seat) => seat switch
    {
        GridSeat g => $"R{g.Row}C{g.Column}" ,
        PolarSeat p => $"环{p.Ring}" ,
        FreeformSeat => $"#{seat.Id[..Math.Min(4 , seat.Id.Length)]}" ,
        _ => seat.Id
    };

    [RelayCommand]
    private async Task LoadSnapshotsAsync ()
    {
        if (SelectedVenue == null) return;
        IsLoading = true;
        await SafeExecuteAsync(async () =>
        {
            var list = await _facade.GetSnapshotsAsync(SelectedVenue.Id);
            Snapshots = new ObservableCollection<SeatingSnapshot>(list);
            SelectedSnapshot = Snapshots.FirstOrDefault();
            StatusMessage = Snapshots.Count > 0
                ? $"会场「{SelectedVenue.Name}」共 {Snapshots.Count} 个快照"
                : $"会场「{SelectedVenue.Name}」暂无快照";
        });
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RollbackAsync ()
    {
        if (SelectedSnapshot == null) return;
        var confirmed = await Dialog.ShowConfirmAsync("确认回滚" ,
            $"确定要回滚到 {SelectedSnapshot.CreatedAt:yyyy-MM-dd HH:mm} 的快照？\n当前座位安排将被覆盖。");
        if (!confirmed) return;

        await SafeExecuteAsync(async () =>
        {
            await _facade.RollbackToSnapshotAsync(SelectedSnapshot.Id);
            StatusMessage = $"已回滚到 {SelectedSnapshot.CreatedAt:yyyy-MM-dd HH:mm}";
            await _navigation.NavigateToAsync(PageKey.SeatingArrangement);
        } , "回滚失败");
    }

    [RelayCommand]
    private async Task DeleteSnapshotAsync ()
    {
        if (SelectedSnapshot == null) return;
        var snapshot = SelectedSnapshot;
        var confirmed = await Dialog.ShowConfirmAsync("确认删除" ,
            $"确定要删除 {snapshot.CreatedAt:yyyy-MM-dd HH:mm} 的快照？");
        if (!confirmed) return;

        await SafeExecuteAsync(async () =>
        {
            await _facade.DeleteSnapshotAsync(snapshot.Id);
            Snapshots.Remove(snapshot);
            SelectedSnapshot = Snapshots.FirstOrDefault();
            StatusMessage = $"已删除，剩余 {Snapshots.Count} 个快照";
        } , "删除快照失败");
    }

    // ── 批量删除 ──

    [RelayCommand]
    private void EnterBatchDeleteMode ()
    {
        var items = Snapshots.Select(s => new SelectableItem(s)).ToArray();
        CheckableItems = new ObservableCollection<SelectableItem>(items);
        IsBatchDeleteMode = true;
        IsAllSelected = false;
        SelectedSnapshot = null;
    }

    [RelayCommand]
    private void ExitBatchDeleteMode ()
    {
        IsBatchDeleteMode = false;
        CheckableItems.Clear();
        IsAllSelected = false;
    }

    [RelayCommand]
    private async Task ConfirmBatchDeleteAsync ()
    {
        var selected = CheckableItems.Where(c => c.IsSelected).Select(c => c.Snapshot).ToList();
        if (selected.Count == 0)
        {
            await Dialog.ShowWarningAsync("批量删除" , "未选中任何快照。");
            return;
        }

        var confirmed = await Dialog.ShowConfirmAsync("批量删除" ,
            $"确定要删除选中的 {selected.Count} 个快照吗？\n此操作不可撤销。");
        if (!confirmed) return;

        await SafeExecuteAsync(async () =>
        {
            foreach (var snap in selected)
            {
                await _facade.DeleteSnapshotAsync(snap.Id);
                Snapshots.Remove(snap);
            }
            ExitBatchDeleteMode();
            SelectedSnapshot = Snapshots.FirstOrDefault();
            StatusMessage = $"已删除 {selected.Count} 个快照，剩余 {Snapshots.Count} 个";
            OnPropertyChanged(nameof(CanEnterBatchDelete));
        } , "批量删除快照失败");
    }

    partial void OnIsAllSelectedChanged (bool value)
    {
        foreach (var item in CheckableItems)
            item.IsSelected = value;
    }
}

public partial class SelectableItem (SeatingSnapshot snapshot) : ObservableObject
{
    public SeatingSnapshot Snapshot { get; } = snapshot;

    [ObservableProperty]
    private bool _isSelected;
}
