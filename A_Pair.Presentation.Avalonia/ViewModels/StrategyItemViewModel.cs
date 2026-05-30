using CommunityToolkit.Mvvm.ComponentModel;
using A_Pair.Presentation.Avalonia.Lang;

namespace A_Pair.Presentation.Avalonia.ViewModels;

/// <summary>
/// 侧栏策略列表中的单个策略项。追踪自身的优先级/启用状态变更。
/// </summary>
public partial class StrategyItemViewModel : ObservableObject
{
    public string Id { get; }
    public string DisplayName { get; }
    public string Source { get; }
    public bool IsBuiltIn { get; }
    public int DefaultPriority { get; }

    [ObservableProperty]
    private int _priority;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _hasChanges;

    public StrategyItemViewModel (
        string id ,
        string displayName ,
        string source ,
        bool isBuiltIn ,
        int priority ,
        int defaultPriority ,
        bool isEnabled)
    {
        Id = id;
        DisplayName = displayName;
        Source = source;
        IsBuiltIn = isBuiltIn;
        _priority = priority;
        DefaultPriority = defaultPriority;
        _isEnabled = isEnabled;
    }

    partial void OnPriorityChanged (int value) => HasChanges = true;
    partial void OnIsEnabledChanged (bool value) => HasChanges = true;

    public string PriorityDisplay => string.Format(Lang.Resources.Strategy_PriorityFmt, Priority);
    public string EnableTooltipDisplay => IsEnabled ? Lang.Resources.Common_Enabled : Lang.Resources.Common_Disabled;

    public void MarkClean () => HasChanges = false;
}
