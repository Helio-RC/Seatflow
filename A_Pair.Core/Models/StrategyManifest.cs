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
}
