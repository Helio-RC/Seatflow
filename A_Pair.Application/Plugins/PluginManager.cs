using System.IO.Compression;
using System.Text.Json;
using A_Pair.Contracts.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Core.Services;
using Microsoft.Extensions.Logging;

namespace A_Pair.Application.Plugins
{
    /// <summary>
    /// 插件管理器，负责从指定目录发现、加载和管理插件包。
    /// 使用 <c>plugins-manifest.json</c> + 策略 <c>manifest.json</c> 双层清单架构。
    /// </summary>
    /// <remarks>
    /// <para>加载方式：</para>
    /// <list type="bullet">
    ///   <item><b>程序集插件（Assembly）</b> — 编译为 .dll 的程序集，通过 <see cref="PluginLoadContext"/> 隔离加载</item>
    ///   <item><b>脚本插件（Script）</b> — Lua 或 C# 脚本文件，通过适配器加载</item>
    /// </list>
    /// <para>加载时自动检测 <see cref="IPluginLifecycle"/> 并调用 <see cref="IPluginLifecycle.InitializeAsync"/>。
    /// 卸载时调用 <see cref="IPluginLifecycle.DisposeAsync"/> 并强制垃圾回收释放程序集资源。</para>
    /// </remarks>
    public class PluginManager : IPluginManager
    {
        private readonly string _pluginsPath;
        private readonly ILogger<PluginManager> _logger;
        private readonly List<PluginLoadContext> _contexts = [];
        private readonly Dictionary<string , Func<string , string , int , IPluginSeatingStrategy>> _scriptAdapters = new(StringComparer.OrdinalIgnoreCase);

        // 包级存储
        private readonly Dictionary<string , LoadedPackageInfo> _loadedPackages = [];
        private readonly Dictionary<string , string> _strategyToPackage = []; // strategyId → packageId
        private readonly Dictionary<string , PluginStrategyEntry> _strategyEntryMap = []; // strategyId → entry
        private readonly Dictionary<string , LoadedPluginInfo> _strategyPlugins = []; // strategyId → LoadedPluginInfo
        private readonly HashSet<string> _loadedPackageDirs = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 初始化插件管理器，确保插件目录存在并注册内置脚本适配器。
        /// </summary>
        public PluginManager (string pluginsPath , ILogger<PluginManager> logger)
        {
            _pluginsPath = pluginsPath;
            _logger = logger;
            Directory.CreateDirectory(_pluginsPath);

            _scriptAdapters["lua"] = (code , name , priority) => new LuaScriptPluginAdapter(code , name , priority);
            _scriptAdapters["csharp"] = (code , name , priority) => new CSharpScriptPluginAdapter(code , name , priority);
        }

        // ─── IPluginManager 实现 ───

        /// <inheritdoc />
        public IReadOnlyDictionary<string , LoadedPackageInfo> LoadedPackages => _loadedPackages;

        /// <inheritdoc />
        public void RegisterScriptAdapter (string scriptType , Func<string , string , int , IPluginSeatingStrategy> factory)
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
            if (!Directory.Exists(_pluginsPath))
                return [];

            var allStrategies = new List<LoadedPluginInfo>();
            if (_strategyPlugins.Count > 0)
                allStrategies.AddRange(_strategyPlugins.Values);

            foreach (var pluginDir in Directory.EnumerateDirectories(_pluginsPath))
            {
                ct.ThrowIfCancellationRequested();

                if (_loadedPackageDirs.Contains(pluginDir))
                    continue;

                try
                {
                    var manifestPath = Path.Combine(pluginDir , "plugins-manifest.json");
                    if (File.Exists(manifestPath))
                    {
                        var strategies = await LoadPackageInternal(pluginDir , manifestPath , category , ct);
                        allStrategies.AddRange(strategies);
                        _loadedPackageDirs.Add(pluginDir);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex , "加载插件包失败：{PluginDir}" , pluginDir);
                }
            }

            return allStrategies;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<LoadedPluginInfo>> RefreshPluginsAsync (string? category = null , CancellationToken ct = default)
        {
            await UnloadAllAsync();
            return await LoadPluginsAsync(category , ct);
        }

        /// <inheritdoc />
        public LoadedPluginInfo? GetLoadedPlugin (string pluginId)
        {
            _strategyPlugins.TryGetValue(pluginId , out var info);
            return info;
        }

        /// <inheritdoc />
        public (LoadedPackageInfo? Package , LoadedPluginInfo? Plugin) FindStrategy (string strategyId)
        {
            if (_strategyToPackage.TryGetValue(strategyId , out var packageId) &&
                _loadedPackages.TryGetValue(packageId , out var pkg) &&
                pkg.Strategies.TryGetValue(strategyId , out var plugin))
            {
                return (pkg , plugin);
            }
            return (null , null);
        }

        /// <inheritdoc />
        public async Task<LoadedPackageInfo?> LoadPackageAsync (string packageId , CancellationToken ct = default)
        {
            if (_loadedPackages.TryGetValue(packageId , out var cached))
                return cached;

            var packageDir = Path.Combine(_pluginsPath , packageId);
            if (!Directory.Exists(packageDir))
                return null;

            var manifestPath = Path.Combine(packageDir , "plugins-manifest.json");
            if (File.Exists(manifestPath))
            {
                await LoadPackageInternal(packageDir , manifestPath , null , ct);
                _loadedPackageDirs.Add(packageDir);
                return _loadedPackages.GetValueOrDefault(packageId);
            }

            return null;
        }

        /// <inheritdoc />
        public async Task RefreshPackageAsync (string packageId , CancellationToken ct = default)
        {
            if (!_loadedPackages.TryGetValue(packageId , out var pkgInfo))
                return;

            await UnloadSinglePackageInternal(pkgInfo);
            RemovePackageFromDictionaries(packageId);

            var packageDir = pkgInfo.PackagePath;
            var manifestPath = Path.Combine(packageDir , "plugins-manifest.json");
            if (File.Exists(manifestPath))
                await LoadPackageInternal(packageDir , manifestPath , null , ct);

            _loadedPackageDirs.Add(packageDir);
        }

        /// <inheritdoc />
        public async Task UnloadPackageAsync (string packageId)
        {
            if (!_loadedPackages.TryGetValue(packageId , out var pkgInfo))
                return;

            await UnloadSinglePackageInternal(pkgInfo);
            RemovePackageFromDictionaries(packageId);

            if (Directory.Exists(pkgInfo.PackagePath))
            {
                try { Directory.Delete(pkgInfo.PackagePath , recursive: true); }
                catch (Exception ex) { _logger.LogWarning(ex , "删除插件包目录失败：{Path}" , pkgInfo.PackagePath); }
            }
        }

        /// <inheritdoc />
        public async Task SetPackageEnabledAsync (string packageId , bool enabled , CancellationToken ct = default)
        {
            if (!_loadedPackages.TryGetValue(packageId , out var pkgInfo))
                throw new InvalidOperationException($"插件包 {packageId} 未加载");

            var enables = pkgInfo.Enables ?? await LoadEnablesAsync(packageId , ct);
            enables.Enabled = enabled;
            pkgInfo.Enables = enables;

            await SaveEnablesAsync(packageId , enables , ct);

            foreach (var (strategyId , pluginInfo) in pkgInfo.Strategies)
            {
                var strategyEnabled = enabled && enables.Strategies.GetValueOrDefault(strategyId , true);
                pluginInfo.Strategy.IsEnabled = strategyEnabled;
            }
        }

        /// <inheritdoc />
        public async Task SetStrategyEnabledAsync (string strategyId , bool enabled , CancellationToken ct = default)
        {
            var (pkg, plugin) = FindStrategy(strategyId);
            if (pkg == null || plugin == null)
                throw new InvalidOperationException($"策略 {strategyId} 未找到");

            plugin.Strategy.IsEnabled = enabled;

            var enables = pkg.Enables ?? await LoadEnablesAsync(pkg.PackageManifest.Id , ct);
            enables.Strategies[strategyId] = enabled;
            pkg.Enables = enables;
            await SaveEnablesAsync(pkg.PackageManifest.Id , enables , ct);
        }

        /// <inheritdoc />
        public async Task<PluginEnables> LoadEnablesAsync (string packageId , CancellationToken ct = default)
        {
            var enablesPath = Path.Combine(_pluginsPath , packageId , "data" , "enables.json");
            if (!File.Exists(enablesPath))
                return new PluginEnables();

            var json = await File.ReadAllTextAsync(enablesPath , ct);
            return JsonSerializer.Deserialize<PluginEnables>(json ,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new PluginEnables();
        }

        /// <inheritdoc />
        public async Task SaveEnablesAsync (string packageId , PluginEnables enables , CancellationToken ct = default)
        {
            var dataDir = Path.Combine(_pluginsPath , packageId , "data");
            Directory.CreateDirectory(dataDir);

            var enablesPath = Path.Combine(dataDir , "enables.json");
            var json = JsonSerializer.Serialize(enables ,
                new JsonSerializerOptions { WriteIndented = true , PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await File.WriteAllTextAsync(enablesPath , json , ct);
        }

        /// <inheritdoc />
        public async Task<string> InstallFromPackageAsync (string packagePath , CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(packagePath))
                throw new FileNotFoundException($"插件包文件不存在：{packagePath}");

            ValidateZipSafety(packagePath);

            var tempDir = Path.Combine(Path.GetTempPath() , $"apair_plugin_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                ZipFile.ExtractToDirectory(packagePath , tempDir , overwriteFiles: true);
                ct.ThrowIfCancellationRequested();

                // 防嵌套：若恰好只有 1 个目录 + 0 个文件，剥离外层
                var entries = Directory.GetFileSystemEntries(tempDir);
                if (entries.Length == 1 && Directory.Exists(entries[0]))
                {
                    var innerDir = entries[0];
                    foreach (var item in Directory.GetFileSystemEntries(innerDir))
                    {
                        var dest = Path.Combine(tempDir , Path.GetFileName(item));
                        if (Directory.Exists(item))
                            CopyDirectoryRecursive(item , dest);
                        else
                            File.Copy(item , dest , overwrite: true);
                    }
                    Directory.Delete(innerDir , recursive: true);
                }

                ct.ThrowIfCancellationRequested();

                // 读取包清单
                var manifestPath = Path.Combine(tempDir , "plugins-manifest.json");
                if (!File.Exists(manifestPath))
                    throw new InvalidDataException("插件包内缺少 plugins-manifest.json 文件");

                var json = await File.ReadAllTextAsync(manifestPath , ct);
                var manifest = JsonSerializer.Deserialize<PluginPackageManifest>(json ,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (manifest is null || string.IsNullOrEmpty(manifest.Id))
                    throw new InvalidDataException("plugins-manifest.json 格式无效：缺少 id 字段");
                var packageId = manifest.Id;

                ct.ThrowIfCancellationRequested();

                var targetDir = Path.Combine(_pluginsPath , packageId);
                if (Directory.Exists(targetDir))
                    throw new InvalidDataException($"插件包 \"{packageId}\" 已存在，请先卸载后再安装");

                Directory.CreateDirectory(targetDir);

                foreach (var filePath in Directory.EnumerateFiles(tempDir , "*" , SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var relativePath = Path.GetRelativePath(tempDir , filePath);
                    var destPath = Path.Combine(targetDir , relativePath);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (destDir != null)
                        Directory.CreateDirectory(destDir);
                    File.Copy(filePath , destPath , overwrite: false);
                }

                // 自动创建 data/enables.json（默认全部启用）
                var enables = new PluginEnables { Enabled = true };
                await SaveEnablesAsync(packageId , enables , ct);

                _logger.LogInformation("插件包 \"{PackageId}\" 安装成功：{TargetDir}" , packageId , targetDir);
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
            foreach (var (_, pkgInfo) in _loadedPackages)
            {
                await UnloadSinglePackageInternal(pkgInfo);
            }

            _loadedPackages.Clear();
            _strategyToPackage.Clear();
            _strategyEntryMap.Clear();
            _strategyPlugins.Clear();
            _loadedPackageDirs.Clear();

            foreach (var c in _contexts)
            {
                c.Unload();
            }
            _contexts.Clear();

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // ─── 私有方法 ───

        /// <summary>
        /// 加载插件包（<c>plugins-manifest.json</c> + 策略 <c>manifest.json</c>）。
        /// </summary>
        private async Task<List<LoadedPluginInfo>> LoadPackageInternal (
            string packageDir , string manifestPath , string? category , CancellationToken ct)
        {
            var results = new List<LoadedPluginInfo>();

            var manifestJson = await File.ReadAllTextAsync(manifestPath , ct);
            var packageManifest = JsonSerializer.Deserialize<PluginPackageManifest>(manifestJson ,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (packageManifest == null || string.IsNullOrEmpty(packageManifest.Id))
                return results;

            if (category != null && !string.Equals(packageManifest.Type , category , StringComparison.OrdinalIgnoreCase))
                return results;

            var packageId = packageManifest.Id;

            var enables = await LoadEnablesAsync(packageId , ct);
            if (string.IsNullOrEmpty(enables.Type))
                enables.Type = packageManifest.Type;

            var pkgInfo = new LoadedPackageInfo
            {
                PackageManifest = packageManifest ,
                PackagePath = packageDir ,
                Enables = enables
            };

            foreach (var entry in packageManifest.Strategies)
            {
                ct.ThrowIfCancellationRequested();

                var strategyManifestPath = Path.Combine(packageDir , entry.Manifest);
                if (!File.Exists(strategyManifestPath))
                {
                    _logger.LogWarning("策略 manifest 文件不存在：{Path}，包：{PkgId}" , strategyManifestPath , packageId);
                    continue;
                }

                var strategyManifestJson = await File.ReadAllTextAsync(strategyManifestPath , ct);
                var strategyManifest = JsonSerializer.Deserialize<StrategyManifest>(strategyManifestJson ,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (strategyManifest == null || string.IsNullOrEmpty(strategyManifest.Id))
                {
                    _logger.LogWarning("策略 manifest 无效：{Path}" , strategyManifestPath);
                    continue;
                }

                var strategy = await LoadStrategyFromEntry(entry , strategyManifest , packageDir , ct);
                if (strategy == null)
                    continue;

                strategy.Priority = strategyManifest.DefaultPriority;

                var isEnabled = enables.Enabled && enables.Strategies.GetValueOrDefault(strategyManifest.Id , true);
                strategy.IsEnabled = isEnabled;

                if (strategy is IPluginLifecycle lifecycle)
                {
                    var host = new PluginHost(_pluginsPath , packageDir);
                    await lifecycle.InitializeAsync(host , ct);
                }

                var pluginInfo = new LoadedPluginInfo
                {
                    Strategy = strategy ,
                    PluginPath = packageDir ,
                    Entry = entry ,
                    StrategyManifest = strategyManifest
                };

                pkgInfo.Strategies[strategyManifest.Id] = pluginInfo;
                _strategyToPackage[strategyManifest.Id] = packageId;
                _strategyEntryMap[strategyManifest.Id] = entry;
                _strategyPlugins[strategyManifest.Id] = pluginInfo;
                results.Add(pluginInfo);

                _logger.LogInformation("加载插件策略：{StrategyId}（包：{PkgId}，类型：{LoadKind}）" ,
                    strategyManifest.Id , packageId ,
                    !string.IsNullOrEmpty(entry.Assembly) ? "assembly" :
                    !string.IsNullOrEmpty(entry.ScriptFile) ? entry.ScriptType ?? "script" : "unknown");
            }

            _loadedPackages[packageId] = pkgInfo;
            return results;
        }

        /// <summary>
        /// 从 PluginStrategyEntry 和 StrategyManifest 加载策略实例。
        /// </summary>
        private async Task<IPluginSeatingStrategy?> LoadStrategyFromEntry (
            PluginStrategyEntry entry , StrategyManifest strategyManifest , string packageDir , CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(entry.ScriptFile) && !string.IsNullOrEmpty(entry.ScriptType))
            {
                var scriptPath = Path.Combine(packageDir , entry.Path , entry.ScriptFile);
                if (!File.Exists(scriptPath))
                {
                    scriptPath = Path.Combine(packageDir , entry.ScriptFile);
                    if (!File.Exists(scriptPath))
                    {
                        _logger.LogWarning("脚本文件不存在：{ScriptFile}" , entry.ScriptFile);
                        return null;
                    }
                }

                var scriptCode = await File.ReadAllTextAsync(scriptPath , ct);

                if (_scriptAdapters.TryGetValue(entry.ScriptType , out var factory))
                    return factory(scriptCode , strategyManifest.DisplayName , strategyManifest.DefaultPriority);

                _logger.LogWarning("未找到脚本适配器：{ScriptType}，策略：{StrategyId}" , entry.ScriptType , strategyManifest.Id);
                return null;
            }
            else if (!string.IsNullOrEmpty(entry.Assembly) && !string.IsNullOrEmpty(entry.EntryType))
            {
                var assemblyPath = Path.Combine(packageDir , entry.Path , entry.Assembly);
                if (!File.Exists(assemblyPath))
                {
                    assemblyPath = Path.Combine(packageDir , entry.Assembly);
                    if (!File.Exists(assemblyPath))
                    {
                        _logger.LogWarning("程序集文件不存在：{Assembly}" , entry.Assembly);
                        return null;
                    }
                }

                var loadContext = new PluginLoadContext(assemblyPath);
                _contexts.Add(loadContext);

                var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
                var type = assembly.GetType(entry.EntryType);
                if (type == null || !typeof(IPluginSeatingStrategy).IsAssignableFrom(type))
                {
                    _logger.LogWarning("入口类型 {Type} 不存在或未实现 IPluginSeatingStrategy" , entry.EntryType);
                    return null;
                }

                return Activator.CreateInstance(type) as IPluginSeatingStrategy;
            }

            _logger.LogWarning("策略 {StrategyId} 缺少加载指令（assembly/entryType 或 scriptFile/scriptType）" , strategyManifest.Id);
            return null;
        }

        /// <summary>
        /// 卸载单个包内的所有策略（dispose lifecycle）。
        /// </summary>
        private async Task UnloadSinglePackageInternal (LoadedPackageInfo pkgInfo)
        {
            foreach (var (_, pluginInfo) in pkgInfo.Strategies)
            {
                if (pluginInfo.Strategy is IPluginLifecycle lifecycle)
                {
                    try
                    {
                        await lifecycle.DisposeAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex , "插件 DisposeAsync 失败：{Id}" , pluginInfo.Strategy.Id);
                    }
                }
            }
        }

        /// <summary>
        /// 验证 ZIP 文件安全性：检查压缩炸弹、总大小、条目数。
        /// </summary>
        private static void ValidateZipSafety (string archivePath)
        {
            const int maxEntryCount = 10000;
            const long maxUncompressedSize = 500L * 1024 * 1024;
            const int maxCompressionRatio = 100;

            using var archive = ZipFile.OpenRead(archivePath);
            var entries = archive.Entries;
            if (entries.Count > maxEntryCount)
                throw new InvalidDataException($"ZIP 条目数 ({entries.Count}) 超过上限 ({maxEntryCount})");

            long totalUncompressed = 0;
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
                    continue;

                totalUncompressed += entry.Length;
                if (totalUncompressed > maxUncompressedSize)
                    throw new InvalidDataException(
                        $"ZIP 解压后总大小 ({totalUncompressed / 1024 / 1024:N0} MB) 超过上限 ({maxUncompressedSize / 1024 / 1024:N0} MB)");

                if (entry.CompressedLength > 0 && entry.Length > 0)
                {
                    var ratio = entry.Length / (double)entry.CompressedLength;
                    if (ratio > maxCompressionRatio)
                        throw new InvalidDataException(
                            $"条目 \"{entry.FullName}\" 压缩比 ({ratio:N0}:1) 超过上限 ({maxCompressionRatio}:1)，疑似 ZIP 炸弹");
                }
            }
        }

        /// <summary>
        /// 递归复制目录（Copy+Delete 模式，兼容跨文件系统场景）。
        /// </summary>
        private static void CopyDirectoryRecursive (string sourceDir , string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.EnumerateFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir , Path.GetFileName(file));
                File.Copy(file , destFile , overwrite: true);
            }
            foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir , Path.GetFileName(subDir));
                CopyDirectoryRecursive(subDir , destSubDir);
            }
        }

        /// <summary>
        /// 从所有内部字典中移除指定包。
        /// </summary>
        private void RemovePackageFromDictionaries (string packageId)
        {
            if (_loadedPackages.TryGetValue(packageId , out var pkgInfo))
            {
                _loadedPackageDirs.Remove(pkgInfo.PackagePath);
                foreach (var strategyId in pkgInfo.Strategies.Keys)
                {
                    _strategyToPackage.Remove(strategyId);
                    _strategyEntryMap.Remove(strategyId);
                    _strategyPlugins.Remove(strategyId);
                }
                _loadedPackages.Remove(packageId);
            }
        }
    }

    /// <summary>
    /// 表示已加载的插件信息，包含策略实例、加载条目和路径。
    /// </summary>
    public class LoadedPluginInfo
    {
        /// <summary>
        /// 策略实例。
        /// </summary>
        public IPluginSeatingStrategy Strategy { get; set; } = default!;

        /// <summary>
        /// 获取插件通用接口实例。当前隐式派生自 <see cref="Strategy"/>。
        /// </summary>
        public IPlugin Plugin => Strategy;

        /// <summary>
        /// 插件所在目录的绝对路径。
        /// </summary>
        public string PluginPath { get; set; } = string.Empty;

        /// <summary>
        /// 策略对应的加载条目（来自 <c>plugins-manifest.json</c> 的 <c>strategies[]</c>）。
        /// </summary>
        public PluginStrategyEntry? Entry { get; set; }

        /// <summary>
        /// 策略的声明式元数据清单（来自策略 <c>manifest.json</c>）。
        /// </summary>
        public StrategyManifest? StrategyManifest { get; set; }
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
