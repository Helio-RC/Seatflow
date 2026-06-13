using System.Text.Json.Serialization;

namespace A_Pair.Application.Plugins
{
    /// <summary>
    /// 插件包中单个策略的加载条目，位于 <c>plugins-manifest.json</c> 的 <c>strategies[]</c> 数组中。
    /// 包含策略子目录路径、manifest 文件路径、以及加载指令（程序集或脚本二选一）。
    /// </summary>
    public class PluginStrategyEntry
    {
        /// <summary>
        /// 策略子目录的相对路径（相对于包根目录）。
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// 策略 manifest 文件的路径（相对于包根目录）。
        /// manifest 内容遵循 <see cref="A_Pair.Core.Models.StrategyManifest"/> 格式。
        /// </summary>
        [JsonPropertyName("manifest")]
        public string Manifest { get; set; } = string.Empty;

        /// <summary>
        /// 程序集文件名（仅程序集插件，相对于 <see cref="Path"/> 或包根目录）。
        /// 与 <c>EntryType</c> 配合使用。
        /// </summary>
        [JsonPropertyName("assembly")]
        public string? Assembly { get; set; }

        /// <summary>
        /// 策略入口类型的完全限定名（仅程序集插件）。
        /// 该类型必须实现 <see cref="A_Pair.Contracts.Interfaces.IPluginSeatingStrategy"/>。
        /// </summary>
        [JsonPropertyName("entryType")]
        public string? EntryType { get; set; }

        /// <summary>
        /// 脚本文件名（仅脚本插件，相对于 <see cref="Path"/> 或包根目录）。
        /// 与 <c>ScriptType</c> 配合使用。
        /// </summary>
        [JsonPropertyName("scriptFile")]
        public string? ScriptFile { get; set; }

        /// <summary>
        /// 脚本类型。支持的值：<c>"lua"</c> 或 <c>"csharp"</c>。
        /// </summary>
        [JsonPropertyName("scriptType")]
        public string? ScriptType { get; set; }
    }
}
