using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using A_Pair.Presentation.Avalonia.Lang;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class StrategyConfigurationViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly ILogger<StrategyConfigurationViewModel> _logger;

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

    public bool HasDetail => SelectedDetail is not null && !string.IsNullOrEmpty(SelectedDetail.Id);

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
    partial void OnEditIsEnabledChanged (bool value)
    {
        if (_suppressChangeTracking) return;
        MarkDetailChanged();
        // 联动侧栏开关
        if (SelectedStrategy is not null)
            SelectedStrategy.IsEnabled = value;
    }
    partial void OnEditHistoryWeightChanged (int value) { if (!_suppressChangeTracking) MarkDetailChanged(); }
    partial void OnEditNeedsFrontRowBonusChanged (int value) { if (!_suppressChangeTracking) MarkDetailChanged(); }
    partial void OnEditFrontRowCountChanged (int value) { if (!_suppressChangeTracking) MarkDetailChanged(); }
    partial void OnEditPreferHorizontalChanged (bool value) { if (!_suppressChangeTracking) MarkDetailChanged(); }
    partial void OnEditAllowVerticalChanged (bool value) { if (!_suppressChangeTracking) MarkDetailChanged(); }

    private void MarkDetailChanged ()
    {
        _hasDetailChanges = true;
        OnPropertyChanged(nameof(HasChanges));
    }

    // ═══════════════ 构造函数 ═══════════════

    
    public string PriorityDisplay => SelectedStrategy != null ? string.Format(Resources.Strategy_PriorityFmt, SelectedStrategy.Priority) : "";
    public string EnableTooltipDisplay => SelectedStrategy != null ? string.Format(Resources.Strategy_EnableFmt, SelectedStrategy.DisplayName) : "";
    public string DetailSourceDisplay => SelectedDetail != null ? string.Format(Resources.Strategy_SourceFmt, SelectedDetail.Source) : "";
    public string DetailAuthorDisplay => SelectedDetail != null ? string.Format(Resources.Strategy_AuthorFmt, SelectedDetail.Author) : "";
    public string DetailCategoryDisplay => SelectedDetail != null ? string.Format(Resources.Strategy_CategoryFmt, SelectedDetail.Category) : "";
    public string DetailDefaultPriorityDisplay => SelectedDetail != null ? string.Format(Resources.Strategy_DefaultPriorityFmt, SelectedDetail.DefaultPriority) : "";

    public StrategyConfigurationViewModel (IApplicationFacade facade , ILogger<StrategyConfigurationViewModel>? logger = null)
    {
        _facade = facade;
        _logger = logger ?? NullLogger<StrategyConfigurationViewModel>.Instance;
        _selectedDetail = new();
        _ = LoadAsync(CancellationToken.None);
    }

    // ═══════════════ 导航离开拦截 ═══════════════

    public override async Task<bool> CanLeaveAsync ()
    {
        if (!HasChanges) return true;

        var choice = await Dialog.ShowConfirmAsync(
            Resources.Strategy_UnsavedChanges ,
            Resources.Strategy_UnsavedChangesMsg);

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
            SelectedDetail = new();
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
            StatusMessage = Resources.Strategy_Loading;

            var displayInfos = await _facade.GetStrategiesAsync(ct);
            var items = displayInfos.Select(d => new StrategyItemViewModel(
                d.Id , d.DisplayName , d.Source , d.IsBuiltIn ,
                d.Priority , d.DefaultPriority , d.IsEnabled)).ToList();

            foreach (var item in items)
            {
                item.PropertyChanged += (_ , e) =>
                {
                    OnPropertyChanged(nameof(HasChanges));
                    // 侧栏开关联动详情面板
                    if (e.PropertyName == nameof(StrategyItemViewModel.IsEnabled)
                        && item == SelectedStrategy)
                    {
                        _suppressChangeTracking = true;
                        EditIsEnabled = item.IsEnabled;
                        _suppressChangeTracking = false;
                    }
                };
            }

            Strategies = new ObservableCollection<StrategyItemViewModel>(items);

            // 检测并修复初始优先级冲突
            var fixedList = DetectAndFixPriorityConflicts();
            if (fixedList.Count > 0)
            {
                var names = string.Join("\n" , fixedList.Select(n => $"• {n}"));
                await Dialog.ShowWarningAsync(
                    Resources.Strategy_PriorityConflictAutoFixed ,
                    string.Format(Resources.Strategy_PriorityConflictMsgFmt, names));
                ReSort();
                StatusMessage = string.Format(Resources.Strategy_LoadedFixedFmt, Strategies.Count, fixedList.Count);
            }
            else
                StatusMessage = string.Format(Resources.Strategy_LoadedFmt, Strategies.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.Data_LoadFailed;
            await Dialog.ShowErrorAsync(Resources.Strategy_LoadFailed , ex.Message);
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
                EditHistoryWeight = GetParamInt(detail.Parameters , "HistoryWeight");
                EditNeedsFrontRowBonus = GetParamInt(detail.Parameters , "NeedsFrontRowBonus");
                EditFrontRowCount = GetParamInt(detail.Parameters , "FrontRowCount");
            }
            if (ShowDeskMateConfig && detail.Parameters is { Count: > 0 })
            {
                EditPreferHorizontal = GetParamBool(detail.Parameters , "PreferHorizontal");
                EditAllowVertical = GetParamBool(detail.Parameters , "AllowVertical");
            }

            _suppressChangeTracking = false;
            _hasDetailChanges = false;
            OnPropertyChanged(nameof(HasChanges));
        }
        catch (Exception ex)
        {
            _suppressChangeTracking = false;
            await Dialog.ShowErrorAsync(Resources.Strategy_DetailLoadFailed , ex.Message);
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
        await ResolveAndSwapPriorityAsync(item , neighbor);
        ReSort();
        StatusMessage = string.Format(Resources.Strategy_MovedUpFmt, item.DisplayName, item.Priority);
    }

    [RelayCommand]
    private async Task MoveDownAsync (StrategyItemViewModel? item)
    {
        if (item is null) return;
        var sorted = Strategies.OrderBy(s => s.Priority).ToList();
        var idx = sorted.IndexOf(item);
        if (idx < 0 || idx >= sorted.Count - 1) return;

        var neighbor = sorted[idx + 1];
        await ResolveAndSwapPriorityAsync(neighbor , item);
        ReSort();
        StatusMessage = string.Format(Resources.Strategy_MovedDownFmt, item.DisplayName, item.Priority);
    }

    private async Task ResolveAndSwapPriorityAsync (
        StrategyItemViewModel first ,
        StrategyItemViewModel second)
    {
        if (first.Priority < second.Priority)
        {
            (first.Priority , second.Priority) = (second.Priority , first.Priority);
            return;
        }

        if (first.Priority == second.Priority)
        {
            var choice = await Dialog.ShowConfirmAsync(
                Resources.Strategy_PriorityConflict ,
                string.Format(Resources.Strategy_PriorityConflictMsg, first.DisplayName, second.DisplayName, first.Priority) + "\n\n" +
                string.Format(Resources.Strategy_PriorityConflictChoice1, first.DisplayName) + "\n" +
                string.Format(Resources.Strategy_PriorityConflictChoice2, second.DisplayName));
            if (choice)
                AssignWithCascade(first , second);
            else
                AssignWithCascade(second , first);
        }
        else
        {
            (first.Priority , second.Priority) = (second.Priority , first.Priority);
        }
    }

    private void AssignWithCascade (StrategyItemViewModel higher , StrategyItemViewModel lower)
    {
        higher.Priority = Math.Max(0 , lower.Priority - 1);
        EnsureUniquePriorities();
    }

    // ═══════════════ 优先级冲突验证 ═══════════════

    /// <summary>
    /// 保存单个策略前检查优先级是否与其他策略冲突。
    /// </summary>
    private async Task<bool> ValidatePriorityBeforeSaveAsync (string strategyId , int newPriority)
    {
        var conflict = Strategies.FirstOrDefault(s =>
            s.Id != strategyId && s.Priority == newPriority);

        if (conflict is null) return true;

        await Dialog.ShowWarningAsync(
            Resources.Strategy_PriorityConflict ,
            string.Format(Resources.Strategy_PriorityTakenFmt, newPriority, conflict.DisplayName));
        return false;
    }

    /// <summary>
    /// 批量保存前检查所有待保存策略之间及与其余策略的优先级冲突。
    /// </summary>
    private async Task<bool> ValidatePriorityBeforeSaveAllAsync (
        List<StrategyItemViewModel> dirtyItems ,
        string? detailStrategyId ,
        int? detailPriority)
    {
        // 构建保存后的优先级快照：dirty 项用新的 Priority，详情编辑用 EditPriority
        var snapshot = Strategies.Select(s =>
        {
            int priority;
            if (s.Id == detailStrategyId && detailPriority.HasValue)
                priority = detailPriority.Value;
            else if (dirtyItems.Any(d => d.Id == s.Id))
                priority = dirtyItems.First(d => d.Id == s.Id).Priority;
            else
                priority = s.Priority;

            return (Id: s.Id , DisplayName: s.DisplayName , Priority: priority);
        }).OrderBy(s => s.Priority).ToList();

        var duplicates = new List<string>();
        for (int i = 1; i < snapshot.Count; i++)
        {
            if (snapshot[i].Priority <= snapshot[i - 1].Priority)
                duplicates.Add(string.Format(Resources.Strategy_DuplicateEntryFmt, snapshot[i].DisplayName, snapshot[i].Priority));
        }

        if (duplicates.Count == 0) return true;

        await Dialog.ShowWarningAsync(
            Resources.Strategy_PriorityConflict ,
            string.Format(Resources.Strategy_DuplicateWarningFmt, string.Join("\n" , duplicates)));
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

        if (!await ValidatePriorityBeforeSaveAsync(SelectedStrategy.Id , EditPriority))
            return;

        var savedName = SelectedDetail.DisplayName;

        try
        {
            IsLoading = true;
            StatusMessage = Resources.Strategy_Saving;

            // 将详情编辑同步到侧栏项
            SelectedStrategy.Priority = EditPriority;
            SelectedStrategy.IsEnabled = EditIsEnabled;

            var config = new StrategyConfig
            {
                Source = SelectedDetail.Source ,
                Priority = EditPriority ,
                IsEnabled = EditIsEnabled ,
                Parameters = CollectDetailParameters()
            };

            await _facade.SaveStrategyConfigAsync(SelectedDetail.Id , config , ct);
            SelectedStrategy.MarkClean();
            _hasDetailChanges = false;
            ReSort();
            OnPropertyChanged(nameof(HasChanges));
            StatusMessage = string.Format(Resources.Strategy_SavedFmt, savedName);
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.Data_SaveFailed;
            await Dialog.ShowErrorAsync(Resources.Strategy_SaveConfigFailed , ex.Message);
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
        // 构建"保存后"的优先级快照（基于当前待保存值，尚未同步到侧栏）
        var dirtyItems = Strategies.Where(s => s.HasChanges).ToList();
        if (_hasDetailChanges && SelectedStrategy is not null && !dirtyItems.Contains(SelectedStrategy))
            dirtyItems.Add(SelectedStrategy);

        // 用待保存的优先级构建快照进行冲突检测
        int? detailPriority = _hasDetailChanges ? EditPriority : null;
        if (!await ValidatePriorityBeforeSaveAllAsync(dirtyItems , SelectedStrategy?.Id , detailPriority))
            return;

        if (dirtyItems.Count == 0)
        {
            StatusMessage = Resources.Strategy_NoChanges;
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = Resources.Strategy_Saving;

            // 同步详情编辑到侧栏项
            if (_hasDetailChanges && SelectedStrategy is not null)
            {
                SelectedStrategy.Priority = EditPriority;
                SelectedStrategy.IsEnabled = EditIsEnabled;
            }

            foreach (var item in dirtyItems)
            {
                var parameters = item.Id == SelectedDetail?.Id && _hasDetailChanges
                    ? CollectDetailParameters()
                    : [];
                var config = new StrategyConfig
                {
                    Source = item.Source ,
                    Priority = item.Priority ,
                    IsEnabled = item.IsEnabled ,
                    Parameters = parameters
                };
                await _facade.SaveStrategyConfigAsync(item.Id , config , ct);
                item.MarkClean();
            }

            _hasDetailChanges = false;
            ReSort();
            OnPropertyChanged(nameof(HasChanges));
            StatusMessage = string.Format(Resources.Strategy_SavedCountFmt, dirtyItems.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.Data_SaveFailed;
            await Dialog.ShowErrorAsync(Resources.Strategy_SaveConfigFailed , ex.Message);
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

        var confirmed = await Dialog.ShowConfirmAsync(Resources.Strategy_RestoreDefaults ,
            string.Format(Resources.Strategy_RestoreDefaultConfirmFmt, SelectedDetail.DisplayName));
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
        StatusMessage = Resources.Strategy_RestoredAndSaved;
    }

    // ═══════════════ 辅助 ═══════════════

    private Dictionary<string , object?> CollectDetailParameters ()
    {
        var p = new Dictionary<string , object?>();
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

    private static int GetParamInt (Dictionary<string , object?> parameters , string key)
    {
        if (parameters.TryGetValue(key , out var v) && v is int i) return i;
        return 0;
    }

    private static bool GetParamBool (Dictionary<string , object?> parameters , string key)
    {
        if (parameters.TryGetValue(key , out var v) && v is bool b) return b;
        return false;
    }
}
