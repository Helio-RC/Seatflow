using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Presentation.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class SnapshotHistoryViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly INavigationService _navigation;

    public string Title { get; } = "历史快照";

    [ObservableProperty]
    private ObservableCollection<VenueItem> _venues = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVenue))]
    private VenueItem? _selectedVenue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSnapshots))]
    private ObservableCollection<SeatingSnapshot> _snapshots = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSnapshot))]
    private SeatingSnapshot? _selectedSnapshot;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "请选择会场查看快照";

    public bool HasSelectedVenue => SelectedVenue != null;
    public bool HasSelectedSnapshot => SelectedSnapshot != null;
    public bool HasSnapshots => Snapshots.Count > 0;
    public bool CanCreateSnapshot => _facade.HasActiveWorkspace;

    public SnapshotHistoryViewModel (IApplicationFacade facade , INavigationService navigation)
    {
        _facade = facade;
        _navigation = navigation;
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
        }, "创建快照失败");
    }

    partial void OnSelectedVenueChanged (VenueItem? value)
    {
        if (value != null)
            _ = LoadSnapshotsAsync();
    }

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
        }, "回滚失败");
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
        }, "删除快照失败");
    }
}
