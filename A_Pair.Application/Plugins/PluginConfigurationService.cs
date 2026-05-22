using System.Text.Json;
using A_Pair.Contracts.Interfaces;

namespace A_Pair.Application.Plugins
{
    /// <summary>
    /// 插件配置服务的默认实现，将插件配置以 JSON 格式存储在插件目录中。
    /// </summary>
    /// <remarks>
    /// 每个插件的配置文件位于 <c><pluginsBasePath>/<pluginId>/config.json</c>。
    /// 支持通过 <see cref="FileSystemWatcher"/> 监视配置文件变更。
    /// </remarks>
    /// <param name="pluginsBasePath">插件根目录路径。</param>
    public class PluginConfigurationService (string pluginsBasePath) : IPluginConfigurationService
    {
        private readonly string _pluginsBasePath = pluginsBasePath;
        private readonly Dictionary<string , FileSystemWatcher> _watchers = [];

        /// <inheritdoc />
        public async Task<T?> LoadConfigurationAsync<T> (string pluginId , CancellationToken cancellationToken = default) where T : class, new()
        {
            var configPath = GetConfigPath(pluginId);
            if (!File.Exists(configPath))
                return new T();

            var json = await File.ReadAllTextAsync(configPath , cancellationToken);
            return JsonSerializer.Deserialize<T>(json);
        }

        /// <inheritdoc />
        public async Task SaveConfigurationAsync<T> (string pluginId , T configuration , CancellationToken cancellationToken = default) where T : class
        {
            var configPath = GetConfigPath(pluginId);
            var configDir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);

            var json = JsonSerializer.Serialize(configuration , new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configPath , json , cancellationToken);
        }

        /// <inheritdoc />
        public void WatchConfiguration (string pluginId , Action<string> onChange)
        {
            var pluginDir = Path.Combine(_pluginsBasePath , pluginId);
            if (!Directory.Exists(pluginDir))
                return;

            // 替换前释放旧监视器
            if (_watchers.TryGetValue(pluginId, out var existing))
                existing.Dispose();

            var watcher = new FileSystemWatcher(pluginDir , "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite
            };
            watcher.Changed += (sender , e) => onChange(pluginId);
            watcher.EnableRaisingEvents = true;
            _watchers[pluginId] = watcher;
        }

        /// <inheritdoc />
        public void StopWatching(string pluginId)
        {
            if (_watchers.TryGetValue(pluginId, out var watcher))
            {
                watcher.Dispose();
                _watchers.Remove(pluginId);
            }
        }

        /// <summary>
        /// 获取指定插件配置文件的完整路径。
        /// </summary>
        /// <param name="pluginId">插件唯一标识符。</param>
        /// <returns>配置文件的完整路径。</returns>
        private string GetConfigPath (string pluginId)
        {
            return Path.Combine(_pluginsBasePath , pluginId , "config.json");
        }
    }
}