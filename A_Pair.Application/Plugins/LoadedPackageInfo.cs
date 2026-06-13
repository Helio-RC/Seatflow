namespace A_Pair.Application.Plugins
{
    /// <summary>
    /// 表示一个已加载的插件包的全部信息，包括包清单、启用状态、已加载的策略字典和路径。
    /// 用于包级的管理和查询。
    /// </summary>
    public class LoadedPackageInfo
    {
        /// <summary>
        /// 插件包清单（<c>plugins-manifest.json</c> 的内容）。
        /// </summary>
        public PluginPackageManifest PackageManifest { get; set; } = new();

        /// <summary>
        /// 插件包在文件系统中的绝对路径。
        /// </summary>
        public string PackagePath { get; set; } = string.Empty;

        /// <summary>
        /// 插件包的运行时启用状态（<c>data/enables.json</c> 的内容）。
        /// 旧格式包此字段为 <c>null</c>。
        /// </summary>
        public PluginEnables? Enables { get; set; }

        /// <summary>
        /// 已加载的策略字典。Key 为策略 ID，Value 为已加载的策略信息。
        /// </summary>
        public Dictionary<string , LoadedPluginInfo> Strategies { get; set; } = [];

        /// <summary>
        /// 是否为旧格式（<c>plugin.manifest.json</c>）的虚拟包。
        /// 旧格式包只有 1 个策略，启用状态写在 manifest 的 <c>enabled</c> 字段中。
        /// </summary>
        public bool IsLegacyFormat { get; set; }
    }
}
