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

    public string Title { get; } = "策略配置";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStrategies))]
    private ObservableCollection<StrategyItemViewModel> _strategies = [];

    public bool HasStrategies => Strategies.Count > 0;

    [ObservableProperty]
    private StrategyItemViewModel? _selectedStrategy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasChanges;

    public StrategyConfigurationViewModel (IApplicationFacade facade)
    {
        _facade = facade;
        _ = LoadAsync(CancellationToken.None);
    }

    // ═══════════════════════════ 加载 ═══════════════════════════

    private async Task LoadAsync (CancellationToken ct)
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在加载策略列表...";

            var dtos = await _facade.GetStrategiesAsync(ct);
            Strategies = new ObservableCollection<StrategyItemViewModel>(
                dtos.Select(d => new StrategyItemViewModel(
                    d.Id, d.Name, d.StrategyTypeKey, d.IsPlugin,
                    d.Priority, d.IsEnabled, d.Configuration)));

            HasChanges = false;
            StatusMessage = $"已加载 {Strategies.Count} 个策略";
        }
        catch (Exception ex)
        {
            StatusMessage = "加载策略列表失败";
            await Dialog.ShowErrorAsync("加载失败", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ═══════════════════════════ 优先级调整 ═══════════════════════════

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

        Strategies.Move(idx, idx - 1);

        var above = Strategies[idx - 1];
        (above.Priority, item.Priority) = (item.Priority, above.Priority);

        HasChanges = true;
        StatusMessage = $"已将「{item.Name}」上移";
    }

    [RelayCommand]
    private void MoveDown (StrategyItemViewModel? item)
    {
        if (item is null) return;
        var idx = Strategies.IndexOf(item);
        if (idx < 0 || idx >= Strategies.Count - 1) return;

        Strategies.Move(idx, idx + 1);

        var below = Strategies[idx + 1];
        (below.Priority, item.Priority) = (item.Priority, below.Priority);

        HasChanges = true;
        StatusMessage = $"已将「{item.Name}」下移";
    }

    // ═══════════════════════════ 保存 ═══════════════════════════

    [RelayCommand]
    private async Task SaveStrategiesAsync (CancellationToken ct)
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在保存策略配置...";

            var dtos = Strategies.Select(s => new StrategyConfigDto
            {
                Id = s.Id,
                Name = s.Name,
                Priority = s.Priority,
                IsEnabled = s.IsEnabled,
                StrategyTypeKey = s.StrategyTypeKey,
                IsPlugin = s.IsPlugin,
                Configuration = s.GetConfigurationSnapshot()
            }).ToList();

            await _facade.SaveStrategiesAsync(dtos, ct);

            foreach (var s in Strategies)
                s.MarkClean();

            HasChanges = false;
            StatusMessage = "策略配置已保存";
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
    private async Task ResetDefaultsAsync ()
    {
        var confirmed = await Dialog.ShowConfirmAsync("恢复默认",
            "确定要恢复所有策略配置到默认值吗？");
        if (!confirmed) return;

        await LoadAsync(CancellationToken.None);
        StatusMessage = "已恢复默认配置（尚未保存）";
    }
}
