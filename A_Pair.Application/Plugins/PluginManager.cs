using System.IO.Compression;
using System.Text.Json;
using A_Pair.Contracts.Interfaces;
using Microsoft.Extensions.Logging;

namespace A_Pair.Application.Plugins
{
    /// <summary>
    /// 插件管理器，负责从指定目录发现、加载和管理插件。
    /// </summary>
    /// <remarks>
    /// 支持两种加载方式的插件：
    /// <list type="bullet">
    ///   <item><b>程序集插件（Assembly）</b> — 编译为 .dll 的程序集，通过 <see cref="PluginLoadContext"/> 隔离加载</item>
    ///   <item><b>脚本插件（Script）</b> — Lua 或 C# 脚本文件，通过适配器加载。脚本适配器可通过 <see cref="RegisterScriptAdapter"/> 注册。</item>
    /// </list>
    /// 加载时自动检测 <see cref="IPluginLifecycle"/> 并调用 <see cref="IPluginLifecycle.InitializeAsync"/>。
    /// 卸载时调用 <see cref="IPluginLifecycle.DisposeAsync"/> 并强制垃圾回收释放程序集资源。
    /// </remarks>
    /// <param name="pluginsPath">插件根目录路径。</param>
    /// <param name="logger">日志记录器。</param>
    public class PluginManager : IPluginManager
    {
        private readonly string _pluginsPath;
        private readonly ILogger<PluginManager> _logger;
        private readonly List<PluginLoadContext> _contexts = [];
        private readonly Dictionary<string , PluginManifest> _loadedManifests = [];
        private readonly Dictionary<string , LoadedPluginInfo> _loadedPlugins = [];
        private readonly List<IPluginLifecycle> _lifecyclePlugins = [];
        private readonly Dictionary<string , Func<string , PluginManifest , IPluginSeatingStrategy>> _scriptAdapters = new(StringComparer.OrdinalIgnoreCase);
        private bool _isLoaded;

        /// <summary>
        /// 初始化插件管理器，确保插件目录存在并注册内置脚本适配器。
        /// </summary>
        public PluginManager (string pluginsPath , ILogger<PluginManager> logger)
        {
            _pluginsPath = pluginsPath;
            _logger = logger;
            Directory.CreateDirectory(_pluginsPath);

            _scriptAdapters["lua"] = (code , manifest) => new LuaScriptPluginAdapter(code , manifest);
            _scriptAdapters["csharp"] = (code , manifest) => new CSharpScriptPluginAdapter(code , manifest);
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string , PluginManifest> LoadedManifests => _loadedManifests;

        /// <summary>
        /// 注册脚本语言适配器。内置已注册 <c>"lua"</c> 和 <c>"csharp"</c>。
        /// </summary>
        public void RegisterScriptAdapter (string scriptType , Func<string , PluginManifest , IPluginSeatingStrategy> factory)
        {
            _scriptAdapters[scriptType] = factory;
        }

        /// <inheritdoc />
        public Task<IEnumerable<LoadedPluginInfo>> LoadStrategyPluginsAsync (CancellationToken ct = default)
        {
            return LoadPluginsAsync("strategy" , ct);
        }

        /// <inheritdoc />
        public async Task<IEnumerable<LoadedPluginInfo>> LoadPluginsAsync (string? category = null , CancellationToken ct = default)
        {
            if (_isLoaded)
                return _loadedPlugins.Values;

            var loadedPlugins = new List<LoadedPluginInfo>();

            if (!Directory.Exists(_pluginsPath))
                return loadedPlugins;

            foreach (var pluginDir in Directory.EnumerateDirectories(_pluginsPath))
            {
                ct.ThrowIfCancellationRequested();

                var manifestPath = Path.Combine(pluginDir , "plugin.manifest.json");
                if (!File.Exists(manifestPath))
                    continue;

                try
                {
                    var manifestJson = await File.ReadAllTextAsync(manifestPath , ct);
                    var manifest = JsonSerializer.Deserialize<PluginManifest>(manifestJson ,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (manifest == null)
                        continue;

                    // 按类别过滤
                    if (category != null && !string.Equals(manifest.Category , category , StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (string.IsNullOrEmpty(manifest.Id))
                        continue;

                    if (string.IsNullOrEmpty(manifest.Category))
                        manifest.Category = "strategy";

                    _loadedManifests[manifest.Id] = manifest;

                    // 根据类别加载插件
                    IPluginSeatingStrategy? strategy = null;
                    if (string.Equals(manifest.Category , "strategy" , StringComparison.OrdinalIgnoreCase))
                        strategy = await LoadPluginInternalAsync(manifest , pluginDir , ct);

                    if (strategy != null)
                    {
                        strategy.Priority = manifest.Priority;
                        strategy.IsEnabled = manifest.Enabled;

                        if (strategy is IPluginLifecycle lifecycle)
                        {
                            var host = new PluginHost(_pluginsPath , pluginDir);
                            await lifecycle.InitializeAsync(host , ct);
                            _lifecyclePlugins.Add(lifecycle);
                        }

                        loadedPlugins.Add(new LoadedPluginInfo
                        {
                            Manifest = manifest ,
                            Strategy = strategy ,
                            PluginPath = pluginDir
                        });

                        _loadedPlugins[manifest.Id] = loadedPlugins[^1];
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex , "加载插件失败：{PluginDir}" , pluginDir);
                }
            }

            _isLoaded = true;
            return loadedPlugins;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<LoadedPluginInfo>> RefreshPluginsAsync (string? category = null , CancellationToken ct = default)
        {
            await UnloadAllAsync();
            _isLoaded = false;
            return await LoadPluginsAsync(category , ct);
        }

        private async Task<IPluginSeatingStrategy?> LoadPluginInternalAsync (PluginManifest manifest , string pluginDir , CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(manifest.ScriptFile))
                return await LoadScriptPluginAsync(manifest , pluginDir , ct);
            else if (!string.IsNullOrEmpty(manifest.Assembly))
                return LoadAssemblyPlugin(manifest , pluginDir);

            return null;
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

        private async Task<IPluginSeatingStrategy?> LoadScriptPluginAsync (PluginManifest manifest , string pluginDir , CancellationToken ct)
        {
            if (string.IsNullOrEmpty(manifest.ScriptFile) || string.IsNullOrEmpty(manifest.ScriptType))
                return null;

            var scriptPath = Path.Combine(pluginDir , manifest.ScriptFile);
            if (!File.Exists(scriptPath))
                return null;

            var scriptCode = await File.ReadAllTextAsync(scriptPath , ct);

            if (_scriptAdapters.TryGetValue(manifest.ScriptType , out var factory))
                return factory(scriptCode , manifest);

            _logger.LogWarning("未找到脚本适配器：{ScriptType}，插件：{PluginId}" , manifest.ScriptType , manifest.Id);
            return null;
        }

        /// <inheritdoc />
        public async Task<string> InstallFromPackageAsync (string packagePath)
        {
            if (!File.Exists(packagePath))
                throw new FileNotFoundException($"插件包文件不存在：{packagePath}");

            var tempDir = Path.Combine(Path.GetTempPath() , $"apair_plugin_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                ZipFile.ExtractToDirectory(packagePath , tempDir , overwriteFiles: true);

                var manifestPath = Path.Combine(tempDir , "plugin.manifest.json");
                if (!File.Exists(manifestPath))
                    throw new InvalidDataException("插件包内缺少 plugin.manifest.json 文件");

                var manifestJson = await File.ReadAllTextAsync(manifestPath);
                var manifest = JsonSerializer.Deserialize<PluginManifest>(manifestJson ,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (manifest is null || string.IsNullOrEmpty(manifest.Id))
                    throw new InvalidDataException("plugin.manifest.json 格式无效：缺少 id 字段");

                var targetDir = Path.Combine(_pluginsPath , manifest.Id);
                if (Directory.Exists(targetDir))
                    throw new InvalidDataException($"插件 \"{manifest.Id}\" 已存在，请先卸载后再安装");

                Directory.CreateDirectory(targetDir);
                foreach (var filePath in Directory.EnumerateFiles(tempDir))
                {
                    var fileName = Path.GetFileName(filePath);
                    File.Copy(filePath , Path.Combine(targetDir , fileName) , overwrite: false);
                }

                _logger.LogInformation("插件 \"{PluginId}\" 安装成功：{TargetDir}" , manifest.Id , targetDir);
                _isLoaded = false;
                return targetDir;
            }
            finally
            {
                try { Directory.Delete(tempDir , recursive: true); }
                catch { /* 忽略清理失败 */ }
            }
        }

        /// <inheritdoc />
        public async Task UnloadAllAsync ()
        {
            foreach (var lifecycle in _lifecyclePlugins)
            {
                try
                {
                    await lifecycle.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex , "插件 DisposeAsync 失败");
                }
            }
            _lifecyclePlugins.Clear();

            _loadedPlugins.Clear();

            foreach (var c in _contexts)
            {
                c.Unload();
            }
            _contexts.Clear();
            _loadedManifests.Clear();
            _isLoaded = false;

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        /// <inheritdoc />
        public LoadedPluginInfo? GetLoadedPlugin (string pluginId)
        {
            _loadedPlugins.TryGetValue(pluginId , out var info);
            return info;
        }

        /// <inheritdoc />
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
        public PluginManifest Manifest { get; set; } = new();

        public IPluginSeatingStrategy Strategy { get; set; } = default!;

        /// <summary>
        /// 获取插件通用接口实例。当前隐式派生自 <see cref="Strategy"/>。
        /// </summary>
        public IPlugin Plugin => Strategy;

        public string PluginPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// 插件宿主的默认实现，在插件初始化时传递给 <see cref="IPluginLifecycle.InitializeAsync"/>。
    /// </summary>
    internal class PluginHost : IPluginHost
    {
        public PluginHost (string pluginsBasePath , string pluginDir)
        {
            PluginDirectory = pluginDir;
            Configuration = new PluginConfigurationService(pluginsBasePath);
        }

        public IPluginConfigurationService Configuration { get; }
        public string PluginDirectory { get; }
    }
}
