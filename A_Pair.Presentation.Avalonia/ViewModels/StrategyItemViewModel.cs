using CommunityToolkit.Mvvm.ComponentModel;

namespace A_Pair.Presentation.Avalonia.ViewModels;

/// <summary>
/// 侧栏策略列表中的单个策略项，用于 ListBox 数据绑定。
/// 只包含侧栏展示所需的轻量属性，详细配置在右侧 StrategyConfigurationViewModel 中编辑。
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
}
