namespace A_Pair.Core.Models;

/// <summary>
/// 策略配置数据传输对象，在 UI 层与应用层之间传递策略的当前状态。
/// 不直接暴露领域层的 <see cref="Strategies.ISeatingStrategy"/> 接口。
/// </summary>
public sealed class StrategyConfigDto
{
    /// <summary>策略唯一标识符。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>策略名称（用于 UI 展示）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>执行优先级，数值越小越先执行。</summary>
    public int Priority { get; set; }

    /// <summary>策略是否启用。</summary>
    public bool IsEnabled { get; set; }

    /// <summary>策略类型鉴别符，用于 View 层选择配置面板模板。</summary>
    public string StrategyTypeKey { get; init; } = string.Empty;

    /// <summary>是否为插件策略。</summary>
    public bool IsPlugin { get; init; }

    /// <summary>策略特有的配置参数（Key = 参数名，Value = 参数值）。</summary>
    public Dictionary<string, object?> Configuration { get; set; } = [];
}
