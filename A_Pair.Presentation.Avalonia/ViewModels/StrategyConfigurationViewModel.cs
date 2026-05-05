using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class StrategyConfigurationViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;

    // ═══════════════ 侧栏列表 ═══════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ObservableCollection<StrategyItemViewModel> _strategies = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private StrategyItemViewModel? _selectedStrategy;

    public bool HasSelection => SelectedStrategy is not null;

    // ═══════════════ 侧栏折叠 ═══════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarCollapsed))]
    private bool _isSidebarExpanded = true;

    public bool IsSidebarCollapsed => !IsSidebarExpanded;

    [ObservableProperty]
    private double _sidebarListWidth = 240;

    private bool _userWantsSidebarExpanded = true;

    // ═══════════════ 详情区域 ═══════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private StrategyDisplayInfo? _selectedDetail;

    public bool HasDetail => SelectedDetail is not null;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    private bool _isLoading;

    public bool IsNotLoading => !IsLoading;

    // ═══════════════ 全局变更：任意策略有变更即为 true ═══════════════

    public bool HasChanges
    {
        get
        {
            if (_hasDetailChanges) return true;
            return Strategies.Any(s => s.HasChanges);
        }
    }

    private bool _hasDetailChanges;
    private bool _suppressChangeTracking;

    // ═══════════════ 详情编辑属性 ═══════════════

    [ObservableProperty]
    private int _editPriority;

    [ObservableProperty]
    private bool _editIsEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFrontRowConfig))]
    private bool _showFrontRowConfig;

    public bool HasFrontRowConfig => ShowFrontRowConfig;

    [ObservableProperty]
    private int _editHistoryWeight;

    [ObservableProperty]
    private int _editNeedsFrontRowBonus;

    [ObservableProperty]
    private int _editFrontRowCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDeskMateConfig))]
    private bool _showDeskMateConfig;

    public bool HasDeskMateConfig => ShowDeskMateConfig;

    [ObservableProperty]
    private bool _editPreferHorizontal;

    [ObservableProperty]
    private bool _editAllowVertical;

    // ── 详情编辑触发 _hasDetailChanges ──

    partial void OnEditPriorityChanged (int value)       { if (!_suppressChangeTracking) MarkDetailChanged(); }
    partial void OnEditIsEnabledChanged (bool value)      { if (!_suppressChangeTracking) MarkDetailChanged(); }
    partial void OnEditHistoryWeightChanged (int value)   { if (!_suppressChangeTracking) MarkDetailChanged(); }
    partial void OnEditNeedsFrontRowBonusChanged (int v)  { if (!_suppressChangeTracking) MarkDetailChanged(); }
    partial void OnEditFrontRowCountChanged (int value)   { if (!_suppressChangeTracking) MarkDetailChanged(); }
    partial void OnEditPreferHorizontalChanged (bool v)   { if (!_suppressChangeTracking) MarkDetailChanged(); }
    partial void OnEditAllowVerticalChanged (bool v)      { if (!_suppressChangeTracking) MarkDetailChanged(); }

    private void MarkDetailChanged ()
    {
        _hasDetailChanges = true;
        OnPropertyChanged(nameof(HasChanges));
    }

    // ═══════════════ 构造函数 ═══════════════

    public StrategyConfigurationViewModel (IApplicationFacade facade)
    {
        _facade = facade;
        _ = LoadAsync(CancellationToken.None);
    }

    // ═══════════════ 导航离开拦截 ═══════════════

    public override async Task<bool> CanLeaveAsync ()
    {
        if (!HasChanges) return true;

        var choice = await Dialog.ShowConfirmAsync(
            "未保存的更改",
            "策略配置有未保存的更改，是否保存？\n\n选择「是」保存并离开\n选择「否」放弃更改并离开");

        if (choice)
            await SaveAllCommand.ExecuteAsync(null);

        return true;
    }

    // ═══════════════ 响应式 ═══════════════

    public void OnWindowWidthChanged (double windowWidth)
    {
        if (windowWidth < 750)
            IsSidebarExpanded = false;
        else
            IsSidebarExpanded = _userWantsSidebarExpanded;
    }

    partial void OnIsSidebarExpandedChanged (bool value)
        => SidebarListWidth = value ? 240 : 120;

    [RelayCommand]
    private void ToggleSidebar ()
    {
        _userWantsSidebarExpanded = !_userWantsSidebarExpanded;
        IsSidebarExpanded = _userWantsSidebarExpanded;
    }

    /// <summary>
    /// 侧栏项属性变更时刷新全局 HasChanges。
    /// </summary>
    partial void OnSelectedStrategyChanged (StrategyItemViewModel? value)
    {
        if (value is null)
        {
            SelectedDetail = null;
            return;
        }
        _ = LoadDetailAsync(value);
    }

    // ═══════════════ 加载 ═══════════════

    private async Task LoadAsync (CancellationToken ct)
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在加载策略列表...";

            var displayInfos = await _facade.GetStrategiesAsync(ct);
            var items = displayInfos.Select(d => new StrategyItemViewModel(
                d.Id, d.DisplayName, d.Source, d.IsBuiltIn,
                d.Priority, d.DefaultPriority, d.IsEnabled)).ToList();

            foreach (var item in items)
                item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(HasChanges));

            Strategies = new ObservableCollection<StrategyItemViewModel>(items);
            StatusMessage = $"已加载 {Strategies.Count} 个策略";
        }
        catch (Exception ex)
        {
            StatusMessage = "加载失败";
            await Dialog.ShowErrorAsync("加载策略列表失败", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadDetailAsync (StrategyItemViewModel item)
    {
        try
        {
            _suppressChangeTracking = true;

            var displayInfos = await _facade.GetStrategiesAsync(CancellationToken.None);
            var detail = displayInfos.FirstOrDefault(d => d.Id == item.Id);
            if (detail is null) return;

            SelectedDetail = detail;
            EditPriority = item.Priority;
            EditIsEnabled = item.IsEnabled;

            ShowFrontRowConfig = detail.Id == "FrontRowRotation";
            ShowDeskMateConfig = detail.Id == "DeskMate";

            if (ShowFrontRowConfig && detail.Parameters is { Count: > 0 })
            {
                EditHistoryWeight = GetParamInt(detail.Parameters, "HistoryWeight");
                EditNeedsFrontRowBonus = GetParamInt(detail.Parameters, "NeedsFrontRowBonus");
                EditFrontRowCount = GetParamInt(detail.Parameters, "FrontRowCount");
            }
            if (ShowDeskMateConfig && detail.Parameters is { Count: > 0 })
            {
                EditPreferHorizontal = GetParamBool(detail.Parameters, "PreferHorizontal");
                EditAllowVertical = GetParamBool(detail.Parameters, "AllowVertical");
            }

            _suppressChangeTracking = false;
            _hasDetailChanges = false;
            OnPropertyChanged(nameof(HasChanges));
        }
        catch (Exception ex)
        {
            _suppressChangeTracking = false;
            await Dialog.ShowErrorAsync("加载详情失败", ex.Message);
        }
    }

    // ═══════════════ 优先级调整 ═══════════════

    public bool CanMoveUp (StrategyItemViewModel? item)
    {
        if (item is null) return false;
        var idx = Strategies.IndexOf(item);
        return idx > 0;
    }

    public bool CanMoveDown (StrategyItemViewModel? item)
    {
        if (item is null) return false;
        var idx = Strategies.IndexOf(item);
        return idx >= 0 && idx < Strategies.Count - 1;
    }

    [RelayCommand]
    private async Task MoveUpAsync (StrategyItemViewModel? item)
    {
        if (item is null) return;
        var sorted = Strategies.OrderBy(s => s.Priority).ToList();
        var idx = sorted.IndexOf(item);
        if (idx <= 0) return;

        var neighbor = sorted[idx - 1];
        await ResolveAndSwapPriorityAsync(item, neighbor);
        ReSort();
        StatusMessage = $"已将「{item.DisplayName}」上移（优先级 {item.Priority}）";
    }

    [RelayCommand]
    private async Task MoveDownAsync (StrategyItemViewModel? item)
    {
        if (item is null) return;
        var sorted = Strategies.OrderBy(s => s.Priority).ToList();
        var idx = sorted.IndexOf(item);
        if (idx < 0 || idx >= sorted.Count - 1) return;

        var neighbor = sorted[idx + 1];
        await ResolveAndSwapPriorityAsync(neighbor, item);
        ReSort();
        StatusMessage = $"已将「{item.DisplayName}」下移（优先级 {item.Priority}）";
    }

    private async Task ResolveAndSwapPriorityAsync (
        StrategyItemViewModel first,
        StrategyItemViewModel second)
    {
        if (first.Priority < second.Priority)
        {
            (first.Priority, second.Priority) = (second.Priority, first.Priority);
            return;
        }

        if (first.Priority == second.Priority)
        {
            var choice = await Dialog.ShowConfirmAsync(
                "优先级冲突",
                $"「{first.DisplayName}」和「{second.DisplayName}」的优先级相同（均为 {first.Priority}）。\n\n" +
                $"选择「是」— {first.DisplayName} 优先执行\n" +
                $"选择「否」— {second.DisplayName} 优先执行");
            if (choice)
                AssignWithCascade(first, second);
            else
                AssignWithCascade(second, first);
        }
        else
        {
            (first.Priority, second.Priority) = (second.Priority, first.Priority);
        }
    }

    private void AssignWithCascade (StrategyItemViewModel higher, StrategyItemViewModel lower)
    {
        higher.Priority = Math.Max(0, lower.Priority - 1);
        var ordered = Strategies.OrderBy(s => s.Priority).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Priority <= ordered[i - 1].Priority)
                ordered[i].Priority = ordered[i - 1].Priority + 1;
        }
    }

    private void ReSort ()
    {
        var selected = SelectedStrategy;
        var sorted = Strategies.OrderBy(s => s.Priority).ToList();
        Strategies = new ObservableCollection<StrategyItemViewModel>(sorted);
        SelectedStrategy = selected;
        OnPropertyChanged(nameof(Strategies));
    }

    // ═══════════════ 保存当前（详情页） ═══════════════

    /// <summary>
    /// 仅保存当前选中策略的配置（优先级 + 启用 + 参数）。
    /// </summary>
    [RelayCommand]
    private async Task SaveCurrentConfigAsync (CancellationToken ct)
    {
        if (SelectedDetail is null || SelectedStrategy is null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "正在保存...";

            // 将详情编辑同步到侧栏项
            SelectedStrategy.Priority = EditPriority;
            SelectedStrategy.IsEnabled = EditIsEnabled;

            var config = new StrategyConfig
            {
                Source = SelectedDetail.Source,
                Priority = EditPriority,
                IsEnabled = EditIsEnabled,
                Parameters = CollectDetailParameters()
            };

            await _facade.SaveStrategyConfigAsync(SelectedDetail.Id, config, ct);
            SelectedStrategy.MarkClean();
            _hasDetailChanges = false;
            OnPropertyChanged(nameof(HasChanges));
            StatusMessage = $"{SelectedDetail.DisplayName} 已保存";
        }
        catch (Exception ex)
        {
            StatusMessage = "保存失败";
            await Dialog.ShowErrorAsync("保存策略配置失败", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ═══════════════ 保存全部（侧栏底部 + 离开弹窗） ═══════════════

    /// <summary>
    /// 保存所有有变更的策略。由侧栏底部按钮和离开弹窗触发。
    /// </summary>
    [RelayCommand]
    private async Task SaveAllAsync (CancellationToken ct)
    {
        try
        {
            var dirtyItems = Strategies.Where(s => s.HasChanges).ToList();

            // 当前详情编辑也计入
            if (_hasDetailChanges && SelectedStrategy is not null)
            {
                SelectedStrategy.Priority = EditPriority;
                SelectedStrategy.IsEnabled = EditIsEnabled;
                if (!dirtyItems.Contains(SelectedStrategy))
                    dirtyItems.Add(SelectedStrategy);
            }

            if (dirtyItems.Count == 0)
            {
                StatusMessage = "没有需要保存的更改";
                return;
            }

            IsLoading = true;
            StatusMessage = "正在保存...";

            foreach (var item in dirtyItems)
            {
                var parameters = item.Id == SelectedDetail?.Id && _hasDetailChanges
                    ? CollectDetailParameters()
                    : [];
                var config = new StrategyConfig
                {
                    Source = item.Source,
                    Priority = item.Priority,
                    IsEnabled = item.IsEnabled,
                    Parameters = parameters
                };
                await _facade.SaveStrategyConfigAsync(item.Id, config, ct);
                item.MarkClean();
            }

            _hasDetailChanges = false;
            OnPropertyChanged(nameof(HasChanges));
            StatusMessage = $"已保存 {dirtyItems.Count} 个策略";
        }
        catch (Exception ex)
        {
            StatusMessage = "保存失败";
            await Dialog.ShowErrorAsync("保存策略配置失败", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ResetConfigAsync ()
    {
        if (SelectedDetail is null) return;

        var confirmed = await Dialog.ShowConfirmAsync("恢复默认",
            $"确定要将「{SelectedDetail.DisplayName}」恢复到默认配置吗？");
        if (!confirmed) return;

        EditPriority = SelectedDetail.DefaultPriority;
        EditIsEnabled = SelectedDetail.DefaultEnabled;

        if (ShowFrontRowConfig)
        {
            EditHistoryWeight = 10;
            EditNeedsFrontRowBonus = 1000;
            EditFrontRowCount = 1;
        }
        if (ShowDeskMateConfig)
        {
            EditPreferHorizontal = true;
            EditAllowVertical = false;
        }

        // 确认后直接保存
        await SaveCurrentConfigCommand.ExecuteAsync(null);
        StatusMessage = "已恢复默认值并保存";
    }

    // ═══════════════ 辅助 ═══════════════

    private Dictionary<string, object?> CollectDetailParameters ()
    {
        var p = new Dictionary<string, object?>();
        if (ShowFrontRowConfig)
        {
            p["HistoryWeight"] = EditHistoryWeight;
            p["NeedsFrontRowBonus"] = EditNeedsFrontRowBonus;
            p["FrontRowCount"] = EditFrontRowCount;
        }
        if (ShowDeskMateConfig)
        {
            p["PreferHorizontal"] = EditPreferHorizontal;
            p["AllowVertical"] = EditAllowVertical;
        }
        return p;
    }

    private static int GetParamInt (Dictionary<string, object?> parameters, string key)
    {
        if (parameters.TryGetValue(key, out var v) && v is int i) return i;
        return 0;
    }

    private static bool GetParamBool (Dictionary<string, object?> parameters, string key)
    {
        if (parameters.TryGetValue(key, out var v) && v is bool b) return b;
        return false;
    }
}
