using System.Text.Json.Serialization;

namespace A_Pair.Application.Plugins
{
    /// <summary>
    /// 插件清单，描述插件的元数据、加载方式和运行时配置。
    /// </summary>
    /// <remarks>
    /// 清单文件 <c>plugin.manifest.json</c> 必须位于插件根目录下。
    /// 支持程序集插件（通过 <see cref="Assembly"/> 和 <see cref="Type"/> 指定入口类型）
    /// 和脚本插件（通过 <see cref="ScriptFile"/> 和 <see cref="ScriptType"/> 指定脚本文件）。
    /// </remarks>
    public class PluginManifest
    {
        /// <summary>
        /// 获取或设置插件唯一标识符。
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置插件显示名称。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置插件功能类别。内置类别：<c>"strategy"</c>、<c>"provider"</c>（预留）、<c>"exporter"</c>（预留）。
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; } = "strategy";

        /// <summary>
        /// 获取或设置插件版本号。
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// 获取或设置插件描述。
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置插件作者。
        /// </summary>
        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置程序集文件名（仅程序集插件）。
        /// </summary>
        [JsonPropertyName("assembly")]
        public string Assembly { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置插件入口类型的完全限定名（仅程序集插件）。
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置插件在策略管道中的执行优先级（数值越小优先级越高）。
        /// </summary>
        [JsonPropertyName("priority")]
        public int Priority { get; set; } = 50;

        /// <summary>
        /// 获取或设置一个值，指示插件是否已启用。
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 获取或设置插件的依赖项列表。
        /// </summary>
        [JsonPropertyName("dependencies")]
        public List<string> Dependencies { get; set; } = [];

        /// <summary>
        /// 获取或设置脚本文件名（仅脚本插件）。
        /// </summary>
        [JsonPropertyName("scriptFile")]
        public string? ScriptFile { get; set; }

        /// <summary>
        /// 获取或设置脚本类型。支持的值：<c>"Lua"</c> 或 <c>"CSharp"</c>。
        /// </summary>
        [JsonPropertyName("scriptType")]
        public string? ScriptType { get; set; }
    }
}