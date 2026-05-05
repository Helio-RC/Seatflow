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
    private bool _isEditing;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    private bool _isLoading;

    public bool IsNotLoading => !IsLoading;

    // ═══════════════ 详情属性（双向绑定用） ═══════════════

    [ObservableProperty]
    private int _editPriority;

    [ObservableProperty]
    private bool _editIsEnabled;

    [ObservableProperty]
    private bool _hasChanges;

    // FrontRowRotation 参数
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

    // DeskMate 参数
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDeskMateConfig))]
    private bool _showDeskMateConfig;

    public bool HasDeskMateConfig => ShowDeskMateConfig;

    [ObservableProperty]
    private bool _editPreferHorizontal;

    [ObservableProperty]
    private bool _editAllowVertical;

    // ═══════════════ 构造函数 ═══════════════

    public StrategyConfigurationViewModel (IApplicationFacade facade)
    {
        _facade = facade;
        _ = LoadAsync(CancellationToken.None);
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
            Strategies = new ObservableCollection<StrategyItemViewModel>(
                displayInfos.Select(d => new StrategyItemViewModel(
                    d.Id , d.DisplayName , d.Source , d.IsBuiltIn ,
                    d.Priority , d.DefaultPriority , d.IsEnabled)));

            StatusMessage = $"已加载 {Strategies.Count} 个策略";
        }
        catch (System.Exception ex)
        {
            StatusMessage = "加载失败";
            await Dialog.ShowErrorAsync("加载策略列表失败" , ex.Message);
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
            var displayInfos = await _facade.GetStrategiesAsync(CancellationToken.None);
            var detail = displayInfos.FirstOrDefault(d => d.Id == item.Id);
            if (detail is null) return;

            SelectedDetail = detail;
            EditPriority = item.Priority;
            EditIsEnabled = item.IsEnabled;

            // 同步策略专属配置
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

            HasChanges = false;
        }
        catch (System.Exception ex)
        {
            await Dialog.ShowErrorAsync("加载详情失败" , ex.Message);
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
    private void MoveUp (StrategyItemViewModel? item)
    {
        if (item is null) return;
        var idx = Strategies.IndexOf(item);
        if (idx <= 0) return;

        Strategies.Move(idx , idx - 1);
        var above = Strategies[idx - 1];
        (above.Priority , item.Priority) = (item.Priority , above.Priority);

        HasChanges = true;
        StatusMessage = $"已将「{item.DisplayName}」上移";
    }

    [RelayCommand]
    private void MoveDown (StrategyItemViewModel? item)
    {
        if (item is null) return;
        var idx = Strategies.IndexOf(item);
        if (idx < 0 || idx >= Strategies.Count - 1) return;

        Strategies.Move(idx , idx + 1);
        var below = Strategies[idx + 1];
        (below.Priority , item.Priority) = (item.Priority , below.Priority);

        HasChanges = true;
        StatusMessage = $"已将「{item.DisplayName}」下移";
    }

    // ═══════════════ 保存 ═══════════════

    [RelayCommand]
    private async Task SaveConfigAsync (CancellationToken ct)
    {
        if (SelectedDetail is null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "正在保存...";

            var parameters = new Dictionary<string , object?>();
            if (ShowFrontRowConfig)
            {
                parameters["HistoryWeight"] = EditHistoryWeight;
                parameters["NeedsFrontRowBonus"] = EditNeedsFrontRowBonus;
                parameters["FrontRowCount"] = EditFrontRowCount;
            }
            if (ShowDeskMateConfig)
            {
                parameters["PreferHorizontal"] = EditPreferHorizontal;
                parameters["AllowVertical"] = EditAllowVertical;
            }

            var config = new StrategyConfig
            {
                Source = SelectedDetail.Source ,
                Priority = EditPriority ,
                IsEnabled = EditIsEnabled ,
                Parameters = parameters
            };

            await _facade.SaveStrategyConfigAsync(SelectedDetail.Id , config , ct);

            // 同步侧栏项
            if (SelectedStrategy is not null)
            {
                SelectedStrategy.Priority = EditPriority;
                SelectedStrategy.IsEnabled = EditIsEnabled;
            }

            SelectedDetail.Priority = EditPriority;
            SelectedDetail.IsEnabled = EditIsEnabled;
            SelectedDetail.Parameters = parameters;

            HasChanges = false;
            StatusMessage = $"{SelectedDetail.DisplayName} 配置已保存";
        }
        catch (System.Exception ex)
        {
            StatusMessage = "保存失败";
            await Dialog.ShowErrorAsync("保存策略配置失败" , ex.Message);
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

        var confirmed = await Dialog.ShowConfirmAsync("恢复默认" ,
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

        HasChanges = true;
        StatusMessage = "已恢复默认值（尚未保存）";
    }

    // ═══════════════ 辅助 ═══════════════

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
