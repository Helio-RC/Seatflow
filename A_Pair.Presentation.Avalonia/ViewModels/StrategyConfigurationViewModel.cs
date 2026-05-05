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

    partial void OnEditPriorityChanged (int value)
    {
        if (_suppressChangeTracking) return;
        MarkDetailChanged();
    }
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

            // 检测并修复初始优先级冲突
            var fixedList = DetectAndFixPriorityConflicts();
            if (fixedList.Count > 0)
            {
                var names = string.Join("\n", fixedList.Select(n => $"• {n}"));
                await Dialog.ShowWarningAsync(
                    "优先级冲突已自动修复",
                    $"以下策略的优先级存在冲突，已自动调整为递增：\n\n{names}");
                StatusMessage = $"已加载 {Strategies.Count} 个策略，自动修复了 {fixedList.Count} 处优先级冲突";
            }
            else
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
        EnsureUniquePriorities();
    }

    // ═══════════════ 优先级冲突验证 ═══════════════

    /// <summary>
    /// 保存单个策略前检查优先级是否与其他策略冲突。
    /// </summary>
    private bool ValidatePriorityBeforeSave (string strategyId , int newPriority)
    {
        var conflict = Strategies.FirstOrDefault(s =>
            s.Id != strategyId && s.Priority == newPriority);

        if (conflict is null) return true;

        _ = Dialog.ShowWarningAsync(
            "优先级冲突",
            $"优先级 {newPriority} 已被「{conflict.DisplayName}」使用。\n\n请修改为不同的优先级值后再保存。");
        return false;
    }

    /// <summary>
    /// 批量保存前检查所有待保存策略之间及与其余策略的优先级冲突。
    /// </summary>
    private bool ValidatePriorityBeforeSaveAll (List<StrategyItemViewModel> dirtyItems)
    {
        // 构建"保存后"的优先级快照：dirty 用新值，其余用当前值
        var snapshot = Strategies.Select(s =>
        {
            var dirty = dirtyItems.FirstOrDefault(d => d.Id == s.Id);
            return (Id: s.Id, DisplayName: s.DisplayName, Priority: dirty?.Priority ?? s.Priority);
        }).OrderBy(s => s.Priority).ToList();

        var duplicates = new List<string>();
        for (int i = 1; i < snapshot.Count; i++)
        {
            if (snapshot[i].Priority <= snapshot[i - 1].Priority)
                duplicates.Add($"• {snapshot[i].DisplayName}（优先级 {snapshot[i].Priority}）");
        }

        if (duplicates.Count == 0) return true;

        _ = Dialog.ShowWarningAsync(
            "优先级冲突",
            $"保存后将出现以下优先级冲突：\n\n{string.Join("\n", duplicates)}\n\n请逐个调整冲突策略的优先级后再保存。");
        return false;
    }

    /// <summary>
    /// 确保所有策略优先级严格递增（无重复、无逆序），返回被修复的策略名列表。
    /// </summary>
    private List<string> DetectAndFixPriorityConflicts ()
    {
        var fixedNames = new List<string>();
        var ordered = Strategies.OrderBy(s => s.Priority).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Priority <= ordered[i - 1].Priority)
            {
                fixedNames.Add(ordered[i].DisplayName);
                ordered[i].Priority = ordered[i - 1].Priority + 1;
            }
        }
        return fixedNames;
    }

    private void EnsureUniquePriorities ()
    {
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
    /// 保存前检测优先级冲突，冲突则拒绝保存。
    /// </summary>
    [RelayCommand]
    private async Task SaveCurrentConfigAsync (CancellationToken ct)
    {
        if (SelectedDetail is null || SelectedStrategy is null) return;

        if (!ValidatePriorityBeforeSave(SelectedStrategy.Id, EditPriority))
            return;

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
    /// 保存前检测所有待保存策略的优先级冲突。
    /// </summary>
    [RelayCommand]
    private async Task SaveAllAsync (CancellationToken ct)
    {
        try
        {
            // 先同步详情编辑
            if (_hasDetailChanges && SelectedStrategy is not null)
            {
                SelectedStrategy.Priority = EditPriority;
                SelectedStrategy.IsEnabled = EditIsEnabled;
            }

            var dirtyItems = Strategies.Where(s => s.HasChanges).ToList();
            if (_hasDetailChanges && SelectedStrategy is not null && !dirtyItems.Contains(SelectedStrategy))
                dirtyItems.Add(SelectedStrategy);

            // 检测所有待保存策略之间及其与其余策略的冲突
            if (!ValidatePriorityBeforeSaveAll(dirtyItems))
                return;

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
