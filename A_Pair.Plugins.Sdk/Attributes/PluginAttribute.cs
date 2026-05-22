namespace A_Pair.Plugins.Sdk.Attributes;

/// <summary>
/// 标记一个类为插件，声明插件的元数据信息。
/// 适用于所有插件类型（策略、数据提供者、导出器等）。
/// 当应用于实现了 <see cref="A_Pair.Contracts.Interfaces.IPlugin"/> 的类时，
/// 该特性数据可用于生成或校验 <c>plugin.manifest.json</c> 文件。
/// </summary>
/// <remarks>
/// 字段与 <c>PluginManifest</c> 完全对应。仅 <see cref="Id"/> 为必选参数。
/// 当插件类同时继承 <see cref="A_Pair.Plugins.Sdk.Abstractions.PluginStrategyBase"/> 时，
/// 基类会自动读取此特性来填充 <c>Id</c>、<c>Name</c>、<c>Priority</c>、<c>IsEnabled</c>。
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class PluginAttribute : Attribute
{
    /// <summary>
    /// 初始化插件特性，指定插件唯一标识符。
    /// </summary>
    /// <param name="id">插件唯一标识符，对应 <c>plugin.manifest.json</c> 中的 <c>id</c> 字段。</param>
    public PluginAttribute(string id)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }

    /// <summary>插件唯一标识符。</summary>
    public string Id { get; }

    /// <summary>插件显示名称（可选，默认为类名）。</summary>
    public string? Name { get; set; }

    /// <summary>插件版本号，如 "1.0.0"（可选，默认 "1.0.0"）。</summary>
    public string? Version { get; set; }

    /// <summary>插件功能描述。</summary>
    public string? Description { get; set; }

    /// <summary>插件作者。</summary>
    public string? Author { get; set; }

    /// <summary>插件功能类别（可选，默认 "strategy"）。</summary>
    public string? Category { get; set; }

    /// <summary>默认执行优先级，数值越小越先执行（可选，默认 50）。</summary>
    public int Priority { get; set; } = 50;

    /// <summary>插件默认是否启用（可选，默认 true）。</summary>
    public bool Enabled { get; set; } = true;
}
