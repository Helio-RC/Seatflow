using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

/// <summary>
/// 侧栏策略列表中的单个策略项。追踪自身的优先级/启用状态变更。
/// 支持嵌套子项（依赖策略在 RandomFill 内部展示）。
/// </summary>
public partial class StrategyItemViewModel : ObservableObject
{
    public string Id { get; }
    public string DisplayName { get; }
    public string Source { get; }
    public bool IsBuiltIn { get; }

    /// <summary>是否为独立策略（false 表示依赖策略，在 RandomFill 上下文中执行）。</summary>
    public bool IsIndependent { get; }

    public int DefaultPriority { get; }

    [ObservableProperty]
    private int _priority;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _hasChanges;

    /// <summary>子策略列表（RandomFill 展开时显示其内部的依赖策略）。</summary>
    [ObservableProperty]
    private ObservableCollection<StrategyItemViewModel>? _children;

    /// <summary>是否展开子策略列表。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChildren))]
    private bool _isExpanded;

    /// <summary>是否被选中（用于高亮显示）。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>是否有可展开的子策略。</summary>
    public bool HasChildren => Children is { Count: > 0 };

    /// <summary>是否为依赖策略的宿主（如 RandomFill）。依赖策略显示为其子项。</summary>
    public bool IsHost => HasChildren;

    /// <summary>当用户点击选中此策略项时的回调。由父 ViewModel 设置，用于子项选中通知。</summary>
    public Action<StrategyItemViewModel>? OnSelected { get; set; }

    public StrategyItemViewModel (
        string id ,
        string displayName ,
        string source ,
        bool isBuiltIn ,
        int priority ,
        int defaultPriority ,
        bool isEnabled ,
        bool isIndependent = true)
    {
        Id = id;
        DisplayName = displayName;
        Source = source;
        IsBuiltIn = isBuiltIn;
        _priority = priority;
        DefaultPriority = defaultPriority;
        _isEnabled = isEnabled;
        IsIndependent = isIndependent;
    }

    partial void OnPriorityChanged (int value) => HasChanges = true;
    partial void OnIsEnabledChanged (bool value) => HasChanges = true;

    public string PriorityDisplay => string.Format(Lang.Resources.Strategy_PriorityFmt , Priority);
    public string EnableTooltipDisplay => IsEnabled ? Lang.Resources.Common_Enabled : Lang.Resources.Common_Disabled;

    public void MarkClean () => HasChanges = false;

    [RelayCommand]
    private void ToggleExpand ()
    {
        IsExpanded = !IsExpanded;
    }

    /// <summary>子项被点击时触发，通知父 ViewModel 切换选中项。</summary>
    [RelayCommand]
    private void SelectSelf ()
    {
        OnSelected?.Invoke(this);
    }
}
