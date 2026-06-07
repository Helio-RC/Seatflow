namespace A_Pair.Core.Models;

/// <summary>
/// 策略的不可变元数据，定义策略的默认配置和描述信息。
/// 内置策略的 manifest 作为嵌入资源编译在 Core.dll 中；插件策略的 manifest 来自 plugin.manifest.json。
/// </summary>
public sealed class StrategyManifest
{
    /// <summary>策略唯一标识符。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>策略内部名称（英文）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>策略显示名称（中文）。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>版本号。</summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>策略功能描述。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>策略作者。</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>策略分类：fill / rotation / grouping / assignment。</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>默认执行优先级（数值越小越先执行）。</summary>
    public int DefaultPriority { get; init; }

    /// <summary>默认是否启用。</summary>
    public bool DefaultEnabled { get; init; } = true;

    /// <summary>策略级全局参数声明（可选）。UI 根据此列表动态渲染参数输入控件。</summary>
    public List<StrategyParameterDefinition>? Parameters { get; init; }

    /// <summary>按数据集/会场的配置块声明（可选）。每个 codeBlock 渲染为一个独立配置区。</summary>
    public List<StrategyCodeBlock>? CodeBlocks { get; init; }

    /// <summary>是否在策略配置页可见（默认 true）。设为 false 时策略不可见、不可用。</summary>
    public bool Visible { get; init; } = true;

    /// <summary>
    /// 策略执行消息的多语言模板（可选）。key 为消息标识符，value 为多语言词典。
    /// 模板中用 {0} {1} 占位，运行时 string.Format 替换。
    /// </summary>
    public Dictionary<string , Dictionary<string , string>>? Messages { get; init; }
}
