using System.Text.Json.Serialization;
using A_Pair.Core.Models;

namespace A_Pair.Application.Plugins
{
    /// <summary>
    /// 旧版插件清单（对应 <c>plugin.manifest.json</c>），描述单策略插件的元数据、加载方式和运行时配置。
    /// </summary>
    /// <remarks>
    /// <para><b>已废弃（Obsolete）</b> — 新插件应使用 <c>plugins-manifest.json</c>（<see cref="PluginPackageManifest"/>）+ 策略 <c>manifest.json</c>（<see cref="StrategyManifest"/>）双层清单格式。</para>
    /// <para>旧清单文件 <c>plugin.manifest.json</c> 必须位于插件根目录下。</para>
    /// <para>支持程序集插件（通过 <see cref="Assembly"/> 和 <see cref="Type"/> 指定入口类型）
    /// 和脚本插件（通过 <see cref="ScriptFile"/> 和 <see cref="ScriptType"/> 指定脚本文件）。</para>
    /// </remarks>
    [Obsolete("新插件应使用 PluginPackageManifest + 策略 manifest.json 双层格式。旧格式仍受支持，通过 ToPackageEntry() 自动转换为虚拟包。")]
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
        /// 获取或设置插件在策略管道中的执行优先级（数值越大优先级越高）。
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

        /// <summary>
        /// 策略级全局参数声明（可选）。UI 根据此列表动态渲染参数输入控件。
        /// </summary>
        [JsonPropertyName("parameters")]
        public List<StrategyParameterDefinition>? Parameters { get; set; }

        /// <summary>
        /// 按数据集/会场的配置块声明（可选）。每个 codeBlock 渲染为一个独立配置区。
        /// </summary>
        [JsonPropertyName("codeBlocks")]
        public List<StrategyCodeBlock>? CodeBlocks { get; set; }

        /// <summary>是否在策略配置页可见（默认 true）。设为 false 时策略不可见、不可用。</summary>
        [JsonPropertyName("visible")]
        public bool Visible { get; set; } = true;

        /// <summary>
        /// 策略执行消息的多语言模板（可选）。key 为消息标识，value 为 {语言: 模板} 词典。
        /// </summary>
        [JsonPropertyName("messages")]
        public Dictionary<string , Dictionary<string , string>>? Messages { get; set; }

        /// <summary>
        /// 是否为独立策略（默认 true）。设为 false 时策略在 RandomFill 上下文中执行。
        /// </summary>
        [JsonPropertyName("isIndependent")]
        public bool IsIndependent { get; set; } = true;

        /// <summary>
        /// Manifest 文件格式版本号，用于运行时版本兼容性校验。
        /// </summary>
        [JsonPropertyName("manifestVersion")]
        public string ManifestVersion { get; set; } = "1.0";

        /// <summary>
        /// 将旧格式的 <see cref="PluginManifest"/> 转换为新格式的 <see cref="PluginPackageManifest"/> 和 <see cref="PluginStrategyEntry"/>。
        /// 旧格式中包 ID = 策略 ID，策略路径为空字符串（即直接位于包根目录）。
        /// </summary>
        /// <returns>包含虚拟包清单和策略条目的元组。</returns>
        public (PluginPackageManifest PackageManifest , PluginStrategyEntry StrategyEntry) ToPackageEntry ()
        {
            var packageManifest = new PluginPackageManifest
            {
                Id = Id ,
                Name = Name ,
                Version = Version ,
                Author = Author ,
                Description = Description ,
                Type = Category ,
                Strategies = []
            };

            var strategyEntry = new PluginStrategyEntry
            {
                Path = string.Empty ,  // 旧格式策略直接位于包根目录
                Manifest = string.Empty , // 旧格式无独立 manifest 文件
                Assembly = string.IsNullOrEmpty(Assembly) ? null : Assembly ,
                EntryType = string.IsNullOrEmpty(Type) ? null : Type ,
                ScriptFile = ScriptFile ,
                ScriptType = ScriptType
            };

            packageManifest.Strategies.Add(strategyEntry);
            return (packageManifest , strategyEntry);
        }
    }
}