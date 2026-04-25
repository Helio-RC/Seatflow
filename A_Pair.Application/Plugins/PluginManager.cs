using System.Text.Json;
using A_Pair.Contracts.Interfaces;

namespace A_Pair.Application.Plugins
{
    /// <summary>
    /// 插件管理器，负责从指定目录发现、加载和管理插件策略。
    /// </summary>
    /// <remarks>
    /// 支持两种类型的插件：
    /// <list type="bullet">
    ///   <item><b>程序集插件（Assembly）</b> — 编译为 .dll 的程序集，通过 <see cref="PluginLoadContext"/> 隔离加载</item>
    ///   <item><b>脚本插件（Script）</b> — Lua 或 C# 脚本文件，通过 <see cref="LuaScriptPluginAdapter"/> 或 <see cref="CSharpScriptPluginAdapter"/> 适配</item>
    /// </list>
    /// 每个插件目录必须包含 <c>plugin.manifest.json</c> 清单文件描述插件元数据和加载方式。
    /// </remarks>
    /// <param name="pluginsPath">插件根目录路径。</param>
    public class PluginManager
    {
        private readonly string _pluginsPath;
        private readonly List<PluginLoadContext> _contexts = [];
        private readonly Dictionary<string , PluginManifest> _loadedManifests = [];

        /// <summary>
        /// 初始化插件管理器，确保插件目录存在。
        /// </summary>
        /// <param name="pluginsPath">插件根目录路径。</param>
        public PluginManager (string pluginsPath)
        {
            _pluginsPath = pluginsPath;
            Directory.CreateDirectory(_pluginsPath);
        }

        /// <summary>
        /// 扫描插件目录并加载所有有效的插件。
        /// </summary>
        /// <returns>已加载的插件信息集合。</returns>
        /// <remarks>
        /// 遍历插件根目录下的每个子目录，查找 <c>plugin.manifest.json</c> 文件。
        /// 根据清单中的 <see cref="PluginManifest.ScriptFile"/> 或 <see cref="PluginManifest.Assembly"/>
        /// 字段决定加载方式。加载失败的插件会被跳过并记录调试信息。
        /// </remarks>
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

        /// <summary>
        /// 加载程序集类型的插件，使用独立的 <see cref="PluginLoadContext"/> 实现隔离。
        /// </summary>
        /// <param name="manifest">插件清单。</param>
        /// <param name="pluginDir">插件目录路径。</param>
        /// <returns>加载成功的插件策略实例；如果加载失败则返回 <c>null</c>。</returns>
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

        /// <summary>
        /// 加载脚本类型的插件（Lua 或 C#）。
        /// </summary>
        /// <param name="manifest">插件清单。</param>
        /// <param name="pluginDir">插件目录路径。</param>
        /// <returns>加载成功的插件策略实例；如果加载失败则返回 <c>null</c>。</returns>
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

        /// <summary>
        /// 卸载所有已加载的插件程序集并释放资源。
        /// </summary>
        /// <remarks>
        /// 调用每个 <see cref="PluginLoadContext.Unload"/> 方法卸载程序集，
        /// 然后强制进行垃圾回收以释放插件占用的内存。
        /// </remarks>
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

        /// <summary>
        /// 获取指定插件 ID 的清单信息。
        /// </summary>
        /// <param name="pluginId">插件唯一标识符。</param>
        /// <returns>插件清单；如果未加载则返回 <c>null</c>。</returns>
        public PluginManifest? GetManifest (string pluginId)
        {
            _loadedManifests.TryGetValue(pluginId , out var manifest);
            return manifest;
        }
    }

    /// <summary>
    /// 表示已加载的插件信息，包含清单、策略实例和路径。
    /// </summary>
    public class LoadedPluginInfo
    {
        /// <summary>
        /// 获取或设置插件清单。
        /// </summary>
        public PluginManifest Manifest { get; set; } = new();

        /// <summary>
        /// 获取或设置插件策略实例。
        /// </summary>
        public IPluginSeatingStrategy Strategy { get; set; } = default!;

        /// <summary>
        /// 获取或设置插件目录路径。
        /// </summary>
        public string PluginPath { get; set; } = string.Empty;
    }
}