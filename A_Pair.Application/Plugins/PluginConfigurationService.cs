using System.Text.Json;

namespace A_Pair.Application.Plugins
{
    public class PluginConfigurationService (string pluginsBasePath) : IPluginConfigurationService
    {
        private readonly string _pluginsBasePath = pluginsBasePath;
        private readonly Dictionary<string , FileSystemWatcher> _watchers = [];

        public async Task<T?> LoadConfigurationAsync<T> (string pluginId , CancellationToken cancellationToken = default) where T : class, new()
        {
            var configPath = GetConfigPath(pluginId);
            if (!File.Exists(configPath))
                return new T();

            var json = await File.ReadAllTextAsync(configPath , cancellationToken);
            return JsonSerializer.Deserialize<T>(json);
        }

        public async Task SaveConfigurationAsync<T> (string pluginId , T configuration , CancellationToken cancellationToken = default) where T : class
        {
            var configPath = GetConfigPath(pluginId);
            var json = JsonSerializer.Serialize(configuration , new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configPath , json , cancellationToken);
        }

        public void WatchConfiguration (string pluginId , Action<string> onChange)
        {
            var pluginDir = Path.Combine(_pluginsBasePath , pluginId);
            if (!Directory.Exists(pluginDir))
                return;

            var watcher = new FileSystemWatcher(pluginDir , "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite
            };
            watcher.Changed += (sender , e) => onChange(pluginId);
            watcher.EnableRaisingEvents = true;
            _watchers[pluginId] = watcher;
        }

        private string GetConfigPath (string pluginId)
        {
            return Path.Combine(_pluginsBasePath , pluginId , "config.json");
        }
    }
}