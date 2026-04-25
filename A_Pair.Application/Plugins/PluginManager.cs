using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using A_Pair.Contracts.Interfaces;

namespace A_Pair.Application.Plugins
{
    public class PluginManager
    {
        private readonly string _pluginsPath;
        private readonly List<PluginLoadContext> _contexts = new();
        private readonly Dictionary<string , PluginManifest> _loadedManifests = new();

        public PluginManager (string pluginsPath)
        {
            _pluginsPath = pluginsPath;
            Directory.CreateDirectory(_pluginsPath);
        }

        public IEnumerable<LoadedPluginInfo> LoadPlugins ()
        {
            var loadedPlugins = new List<LoadedPluginInfo>();

            foreach (var pluginDir in Directory.EnumerateDirectories(_pluginsPath))
            {
                var manifestPath = Path.Combine(pluginDir , "plugin.manifest.json");
                if (!File.Exists(manifestPath))
                    continue;

                try
                {
                    var manifestJson = File.ReadAllText(manifestPath);
                    var manifest = JsonSerializer.Deserialize<PluginManifest>(manifestJson , new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (manifest == null)
                        continue;

                    // 验证清单
                    if (string.IsNullOrEmpty(manifest.Id) || string.IsNullOrEmpty(manifest.Type))
                        continue;

                    _loadedManifests[manifest.Id] = manifest;

                    // 根据插件类型加载
                    IPluginSeatingStrategy? strategy = null;

                    if (!string.IsNullOrEmpty(manifest.ScriptFile))
                    {
                        strategy = LoadScriptPlugin(manifest , pluginDir);
                    }
                    else if (!string.IsNullOrEmpty(manifest.Assembly))
                    {
                        strategy = LoadAssemblyPlugin(manifest , pluginDir);
                    }

                    if (strategy != null)
                    {
                        strategy.Priority = manifest.Priority;
                        strategy.IsEnabled = manifest.Enabled;
                        loadedPlugins.Add(new LoadedPluginInfo
                        {
                            Manifest = manifest ,
                            Strategy = strategy ,
                            PluginPath = pluginDir
                        });
                    }
                }
                catch (Exception ex)
                {
                    // 记录日志：加载插件失败
                    System.Diagnostics.Debug.WriteLine($"Failed to load plugin from {pluginDir}: {ex.Message}");
                }
            }

            return loadedPlugins;
        }

        private IPluginSeatingStrategy? LoadAssemblyPlugin (PluginManifest manifest , string pluginDir)
        {
            var assemblyPath = Path.Combine(pluginDir , manifest.Assembly);
            if (!File.Exists(assemblyPath))
                return null;

            var loadContext = new PluginLoadContext(assemblyPath);
            _contexts.Add(loadContext);

            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var type = assembly.GetType(manifest.Type);
            if (type == null || !typeof(IPluginSeatingStrategy).IsAssignableFrom(type))
                return null;

            return Activator.CreateInstance(type) as IPluginSeatingStrategy;
        }

        private IPluginSeatingStrategy? LoadScriptPlugin (PluginManifest manifest , string pluginDir)
        {
            if (string.IsNullOrEmpty(manifest.ScriptFile))
                return null;

            var scriptPath = Path.Combine(pluginDir , manifest.ScriptFile);
            if (!File.Exists(scriptPath))
                return null;

            var scriptCode = File.ReadAllText(scriptPath);

            // 根据脚本类型创建对应策略
            IPluginSeatingStrategy? strategy = manifest.ScriptType?.ToLowerInvariant() switch
            {
                "lua" => new LuaScriptPluginAdapter(scriptCode , manifest),
                "csharp" => new CSharpScriptPluginAdapter(scriptCode , manifest),
                _ => null
            };

            return strategy;
        }

        public void UnloadAll ()
        {
            foreach (var c in _contexts)
            {
                c.Unload();
            }
            _contexts.Clear();
            _loadedManifests.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public PluginManifest? GetManifest (string pluginId)
        {
            _loadedManifests.TryGetValue(pluginId , out var manifest);
            return manifest;
        }
    }

    public class LoadedPluginInfo
    {
        public PluginManifest Manifest { get; set; } = new();
        public IPluginSeatingStrategy Strategy { get; set; } = default!;
        public string PluginPath { get; set; } = string.Empty;
    }
}