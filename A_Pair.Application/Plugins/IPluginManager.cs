using A_Pair.Contracts.Interfaces;

namespace A_Pair.Application.Plugins;

/// <summary>
/// 插件管理器接口，负责插件的发现、加载、卸载和安装。
/// </summary>
public interface IPluginManager
{
    /// <summary>
    /// 扫描插件目录并异步加载所有排座策略插件。
    /// 这是 <see cref="LoadPluginsAsync"/> 的便捷方法，仅返回类别为 "strategy" 的插件。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>已加载的策略插件信息集合。</returns>
    Task<IEnumerable<LoadedPluginInfo>> LoadStrategyPluginsAsync(CancellationToken ct = default);

    /// <summary>
    /// 扫描插件目录并异步加载所有插件，可按类别过滤。
    /// </summary>
    /// <param name="category">
    /// 要加载的插件类别。为 <c>null</c> 时加载所有插件。
    /// 内置类别：<c>"strategy"</c>、<c>"provider"</c>（预留）、<c>"exporter"</c>（预留）。
    /// </param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>已加载的插件信息集合。</returns>
    Task<IEnumerable<LoadedPluginInfo>> LoadPluginsAsync(string? category = null, CancellationToken ct = default);

    /// <summary>
    /// 异步卸载所有已加载的插件并释放资源（包括调用 <see cref="IPluginLifecycle.DisposeAsync"/>）。
    /// </summary>
    Task UnloadAllAsync();

    /// <summary>
    /// 获取指定插件 ID 的清单信息。
    /// </summary>
    PluginManifest? GetManifest(string pluginId);

    /// <summary>
    /// 从 <c>.apairplugin</c> 包文件安装插件到插件目录。
    /// </summary>
    /// <param name="packagePath"><c>.apairplugin</c> 文件路径。</param>
    /// <returns>安装后的插件目录路径。</returns>
    Task<string> InstallFromPackageAsync(string packagePath);

    /// <summary>
    /// 获取所有已加载插件的清单字典（键为插件 ID）。
    /// </summary>
    IReadOnlyDictionary<string, PluginManifest> LoadedManifests { get; }

    /// <summary>
    /// 从缓存中获取已加载的插件信息，不触发重新扫描。
    /// </summary>
    /// <param name="pluginId">插件唯一标识符。</param>
    /// <returns>已加载的插件信息；若未加载则返回 <c>null</c>。</returns>
    LoadedPluginInfo? GetLoadedPlugin(string pluginId);
}
