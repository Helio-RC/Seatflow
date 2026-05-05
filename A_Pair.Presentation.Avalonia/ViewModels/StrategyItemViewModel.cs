using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class StrategyItemViewModel : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    public string StrategyTypeKey { get; }
    public bool IsPlugin { get; }

    [ObservableProperty]
    private int _priority;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isExpanded;

    private readonly Dictionary<string, object?> _configuration;

    public bool IsDirty { get; private set; }

    // ── 类型判断属性（用于 View 层条件显示配置面板） ──

    public bool HasFrontRowConfig => StrategyTypeKey == "FrontRowRotation";
    public bool HasDeskMateConfig => StrategyTypeKey == "DeskMate";
    public bool HasNoConfig => StrategyTypeKey is "RandomFill" or "FixedSeat";
    public bool HasPluginConfig => IsPlugin;

    // ── FrontRowRotation 配置属性 ──

    public int HistoryWeight
    {
        get => GetConfigInt("HistoryWeight");
        set { SetConfig("HistoryWeight", value); OnPropertyChanged(); }
    }

    public int NeedsFrontRowBonus
    {
        get => GetConfigInt("NeedsFrontRowBonus");
        set { SetConfig("NeedsFrontRowBonus", value); OnPropertyChanged(); }
    }

    public int FrontRowCount
    {
        get => GetConfigInt("FrontRowCount");
        set { SetConfig("FrontRowCount", value); OnPropertyChanged(); }
    }

    // ── DeskMate 配置属性 ──

    public bool PreferHorizontal
    {
        get => GetConfigBool("PreferHorizontal");
        set { SetConfig("PreferHorizontal", value); OnPropertyChanged(); }
    }

    public bool AllowVertical
    {
        get => GetConfigBool("AllowVertical");
        set { SetConfig("AllowVertical", value); OnPropertyChanged(); }
    }

    public StrategyItemViewModel (
        string id ,
        string name ,
        string strategyTypeKey ,
        bool isPlugin ,
        int priority ,
        bool isEnabled ,
        Dictionary<string , object?> configuration)
    {
        Id = id;
        Name = name;
        StrategyTypeKey = strategyTypeKey;
        IsPlugin = isPlugin;
        _priority = priority;
        _isEnabled = isEnabled;
        _configuration = new Dictionary<string , object?>(configuration);
    }

    partial void OnPriorityChanged (int value) => IsDirty = true;
    partial void OnIsEnabledChanged (bool value) => IsDirty = true;

    public Dictionary<string , object?> GetConfigurationSnapshot ()
        => new(_configuration);

    public void MarkClean () => IsDirty = false;

    private int GetConfigInt (string key)
    {
        if (_configuration.TryGetValue(key, out var v) && v is int i)
            return i;
        return 0;
    }

    private bool GetConfigBool (string key)
    {
        if (_configuration.TryGetValue(key, out var v) && v is bool b)
            return b;
        return false;
    }

    private void SetConfig (string key , object? value)
    {
        _configuration[key] = value;
        IsDirty = true;
    }
}
