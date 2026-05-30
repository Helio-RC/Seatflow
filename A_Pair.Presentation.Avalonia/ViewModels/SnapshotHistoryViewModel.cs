using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.DomainServices;
using A_Pair.Core.Models;
using A_Pair.Presentation.Avalonia.Lang;
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

    public string Title { get; } = Resources.Snapshot_Title;

    [ObservableProperty]
    private ObservableCollection<VenueItem> _venues = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVenue))]
    [NotifyPropertyChangedFor(nameof(NoSnapshotDisplay))]
    private VenueItem? _selectedVenue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSnapshots))]
    [NotifyPropertyChangedFor(nameof(CanEnterBatchDelete))]
    [NotifyPropertyChangedFor(nameof(SnapshotCountDisplay))]
    private ObservableCollection<SeatingSnapshot> _snapshots = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSnapshot))]
    private SeatingSnapshot? _selectedSnapshot;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = Resources.Snapshot_VenueHint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBatchMode))]
    [NotifyPropertyChangedFor(nameof(CanEnterBatchDelete))]
    private bool _isBatchDeleteMode;

    public bool IsNotBatchMode => !IsBatchDeleteMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectAllDisplay))]
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

    // ── 完整性状态 ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRollbackDisabled))]
    private bool _isVenueDeleted;

    [ObservableProperty]
    private bool _isVenueChanged;

    [ObservableProperty]
    private bool _isDataChanged;

    [ObservableProperty]
    private string _venueWarningText = string.Empty;

    public bool HasRollbackDisabled => IsVenueDeleted;

    public bool HasSelectedVenue => SelectedVenue != null;
    public bool HasSelectedSnapshot => SelectedSnapshot != null;
    public bool HasSnapshots => Snapshots.Count > 0;
    public bool CanCreateSnapshot => _facade.HasActiveWorkspace;
    public bool CanEnterBatchDelete => HasSnapshots && !IsBatchDeleteMode;

    
    public string SnapshotCountDisplay => string.Format(Resources.Snapshot_CountFmt, Snapshots.Count);
    public string NoSnapshotDisplay => SelectedVenue != null ? string.Format(Resources.Snapshot_NoSnapshotsFmt, SelectedVenue.Name) : "";
    public string SelectAllDisplay => string.Format(Resources.Snapshot_SelectAllFmt, CheckableItems.Count);
    public string PreviewSeatCountDisplay => SelectedSnapshot?.SeatAssignments?.Count > 0 ? string.Format(Resources.Snapshot_SeatCountFmt, SelectedSnapshot.SeatAssignments.Count) : "";
    public string SnapshotSeatCountDisplay => string.Format(Resources.Snapshot_SeatCountFmt, (SelectedSnapshot?.SeatAssignments?.Count ?? 0));

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
        var previousVenueId = SelectedVenue?.Id;

        // 清空所有已加载数据
        Snapshots = [];
        PreviewSeats = [];
        PreviewOverlays = [];
        IsVenueDeleted = false;
        IsVenueChanged = false;
        IsDataChanged = false;
        VenueWarningText = string.Empty;
        SelectedSnapshot = null;
        SelectedVenue = null;

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
            StatusMessage = string.Format(Resources.Snapshot_VenuesLoadedFmt, items.Count);

            // 重新选中之前的会场
            if (previousVenueId != null)
                SelectedVenue = items.FirstOrDefault(v => v.Id == previousVenueId);
        });
    }



    [RelayCommand]
    private async Task CreateSnapshotAsync ()
    {
        var (confirmed , description) = await Dialog.ShowInputAsync(
            Resources.Snapshot_CreateTitle , Resources.Snapshot_CreatePrompt , string.Format(Resources.Snapshot_ManualSnapshotFmt, DateTime.Now.ToString("yyyy-MM-dd HH:mm")));
        if (!confirmed || string.IsNullOrWhiteSpace(description)) return;

        await SafeExecuteAsync(async () =>
        {
            var snapshot = await _facade.CreateSnapshotAsync(description);
            if (snapshot == null)
            {
                await Dialog.ShowWarningAsync(Resources.Snapshot_CreateFailed , Resources.Snapshot_NoWorkspace);
                return;
            }
            if (SelectedVenue != null)
                await LoadSnapshotsAsync();
            StatusMessage = Resources.Snapshot_Created;
        } , Resources.Snapshot_CreateFailed);
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

        // 重置完整性状态
        IsVenueDeleted = false;
        IsVenueChanged = false;
        IsDataChanged = false;
        VenueWarningText = string.Empty;

        try
        {
            var layout = await _facade.LoadVenueAsync(snapshot.LayoutId);
            if (layout == null || layout.Seats.Count == 0)
            {
                IsVenueDeleted = true;
                VenueWarningText = Resources.Snapshot_VenueDeletedPreview;
                PreviewSeats = seats;
                PreviewOverlays = overlays;
                return;
            }

            // 检测会场是否变更（对比哈希）
            if (snapshot.Metadata.TryGetValue("venueHash" , out var snapHash) &&
                snapHash is string sh && !string.IsNullOrEmpty(sh))
            {
                var curHash = await _facade.GetVenueHashAsync(snapshot.LayoutId);
                if (curHash != null && curHash != sh)
                {
                    IsVenueChanged = true;
                    VenueWarningText = Resources.Snapshot_VenueChangedWarning;
                }
            }

            // 收集当前所有数据集中的学生 ID 和姓名
            var datasets = await _facade.ListStudentDatasetsAsync();
            var foundIds = new HashSet<string>();
            foreach (var ds in datasets)
            {
                var students = await _facade.LoadStudentDatasetAsync(ds.Id);
                if (students != null)
                    foreach (var s in students)
                        foundIds.Add(s.Id);
            }

            // 检测数据变更：快照中学生 ID 是否在当前数据集中仍存在
            var assignments = snapshot.SeatAssignments;
            var snapshotStudentIds = assignments.Values.Where(v => !string.IsNullOrEmpty(v)).ToHashSet();
            var missingIds = snapshotStudentIds.Where(id => !foundIds.Contains(id)).ToHashSet();
            if (missingIds.Count > 0)
            {
                IsDataChanged = true;
                VenueWarningText = string.IsNullOrEmpty(VenueWarningText)
                    ? Resources.Snapshot_DataChangedText : VenueWarningText;
            }

            var metadata = layout.Metadata!;

            // 从 Metadata 中读取 studentNames 字典（studentId → studentName）
            var studentNames = new Dictionary<string , string>();
            if (snapshot.Metadata.TryGetValue("studentNames" , out var rawNames) &&
                rawNames is System.Text.Json.JsonElement je &&
                je.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in je.EnumerateObject())
                    studentNames[prop.Name] = prop.Value.GetString() ?? prop.Name;
            }

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
                studentNames.TryGetValue(sid ?? "" , out var sname);
                bool isDataStale = occupied && missingIds.Contains(sid!);
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
                    StudentName = occupied ? (sname ?? sid) : null ,
                    OccupancyStatus = occupied ? SeatOccupancyStatus.Occupied : SeatOccupancyStatus.Empty ,
                    IsDataStale = isDataStale ,
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
        PolarSeat p => string.Format(Resources.Snapshot_PolarLabelFmt, p.Ring) ,
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
                ? string.Format(Resources.Snapshot_VenueHintFmt, SelectedVenue.Name, Snapshots.Count)
                : string.Format(Resources.Snapshot_VenueEmptyFmt, SelectedVenue.Name);
        });
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RollbackAsync ()
    {
        if (SelectedSnapshot == null) return;
        var confirmed = await Dialog.ShowConfirmAsync(Resources.Snapshot_RollbackTitle ,
            string.Format(Resources.Snapshot_RollbackMsgFmt, SelectedSnapshot.CreatedAt.ToString("yyyy-MM-dd HH:mm")));
        if (!confirmed) return;

        await SafeExecuteAsync(async () =>
        {
            await _facade.RollbackToSnapshotAsync(SelectedSnapshot.Id);
            StatusMessage = string.Format(Resources.Snapshot_RollbackDoneFmt, SelectedSnapshot.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
            await _navigation.NavigateToAsync(PageKey.SeatingArrangement);
        } , Resources.Snapshot_RollbackFailed);
    }

    [RelayCommand]
    private async Task DeleteSnapshotAsync ()
    {
        if (SelectedSnapshot == null) return;
        var snapshot = SelectedSnapshot;
        var confirmed = await Dialog.ShowConfirmAsync(Resources.Data_DeleteConfirm ,
            string.Format(Resources.Snapshot_DeleteMsgFmt, snapshot.CreatedAt.ToString("yyyy-MM-dd HH:mm")));
        if (!confirmed) return;

        await SafeExecuteAsync(async () =>
        {
            await _facade.DeleteSnapshotAsync(snapshot.Id);
            Snapshots.Remove(snapshot);
            SelectedSnapshot = Snapshots.FirstOrDefault();
            StatusMessage = string.Format(Resources.Snapshot_DeletedFmt, Snapshots.Count);
        } , Resources.Snapshot_DeleteFailed);
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
            await Dialog.ShowWarningAsync(Resources.Snapshot_BatchDeleteTitle , Resources.Snapshot_NoSelection);
            return;
        }

        var confirmed = await Dialog.ShowConfirmAsync(Resources.Snapshot_BatchDeleteTitle ,
            string.Format(Resources.Snapshot_BatchDeleteMsgFmt, selected.Count));
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
            StatusMessage = string.Format(Resources.Snapshot_BatchDeletedFmt, selected.Count, Snapshots.Count);
            OnPropertyChanged(nameof(CanEnterBatchDelete));
        } , Resources.Snapshot_BatchDeleteFailed);
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
