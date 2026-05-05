namespace A_Pair.Core.Models;

/// <summary>
/// 策略的可变运行时配置，由用户通过 UI 修改并持久化到 AppData。
/// </summary>
public sealed class StrategyConfig
{
    /// <summary>配置来源："builtin" 或 "plugin:{pluginId}"。</summary>
    public string Source { get; set; } = "builtin";

    /// <summary>当前执行优先级。</summary>
    public int Priority { get; set; }

    /// <summary>当前是否启用。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>策略特有的配置参数。</summary>
    public Dictionary<string, object?> Parameters { get; set; } = [];
}
