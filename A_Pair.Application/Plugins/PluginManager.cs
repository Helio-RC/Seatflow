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
    /// 支持新旧两种包格式的双路径扫描。
    /// </summary>
    /// <remarks>
    /// <para><b>新格式（推荐）</b>：<c>plugins-manifest.json</c> + 策略 <c>manifest.json</c>，支持多策略包。</para>
    /// <para><b>旧格式（兼容）</b>：<c>plugin.manifest.json</c>，自动转换为虚拟包（1 包 = 1 策略）。</para>
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
        private readonly Dictionary<string , Func<string , PluginManifest , IPluginSeatingStrategy>> _scriptAdapters = new(StringComparer.OrdinalIgnoreCase);

        // 包级存储
        private readonly Dictionary<string , LoadedPackageInfo> _loadedPackages = [];
        private readonly Dictionary<string , string> _strategyToPackage = []; // strategyId → packageId
        private readonly Dictionary<string , PluginStrategyEntry> _strategyEntryMap = []; // strategyId → entry

        // 旧字典 — 由 SyncLegacyDictionaries() 同步，供外部（ApplicationFacade 等）按旧 API 访问
        private readonly Dictionary<string , PluginManifest> _loadedManifests = [];
        private readonly Dictionary<string , LoadedPluginInfo> _loadedPlugins = [];

        private readonly HashSet<string> _loadedPackageDirs = new(StringComparer.OrdinalIgnoreCase);

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

        // ─── IPluginManager 实现 ───

        /// <inheritdoc />
        public IReadOnlyDictionary<string , LoadedPackageInfo> LoadedPackages => _loadedPackages;

        /// <inheritdoc />
        [Obsolete("新代码应使用 FindStrategy 获取包级信息")]
        public IReadOnlyDictionary<string , PluginManifest> LoadedManifests => _loadedManifests;

        /// <summary>
        /// 注册脚本语言适配器。
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
            if (!Directory.Exists(_pluginsPath))
                return [];

            // 总是包含已缓存的策略（安装后会有新目录加入，但缓存依然有效）
            var allStrategies = new List<LoadedPluginInfo>();
            if (_loadedPlugins.Count > 0)
                allStrategies.AddRange(_loadedPlugins.Values);

            foreach (var pluginDir in Directory.EnumerateDirectories(_pluginsPath))
            {
                ct.ThrowIfCancellationRequested();

                // 跳过已成功加载的目录
                if (_loadedPackageDirs.Contains(pluginDir))
                    continue;

                try
                {
                    var newManifestPath = Path.Combine(pluginDir , "plugins-manifest.json");
                    var oldManifestPath = Path.Combine(pluginDir , "plugin.manifest.json");

                    if (File.Exists(newManifestPath))
                    {
                        // 新格式
                        var strategies = await LoadNewFormatPackage(pluginDir , newManifestPath , category , ct);
                        allStrategies.AddRange(strategies);
                        _loadedPackageDirs.Add(pluginDir);
                    }
                    else if (File.Exists(oldManifestPath))
                    {
                        // 旧格式（兼容）
                        var strategy = await LoadOldFormatPackage(pluginDir , oldManifestPath , category , ct);
                        if (strategy != null)
                            allStrategies.Add(strategy);
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
                    // 不加入 _loadedPackageDirs，下次调用会重试
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
        [Obsolete("新代码应使用 FindStrategy 获取包级信息")]
        public PluginManifest? GetManifest (string pluginId)
        {
            _loadedManifests.TryGetValue(pluginId , out var manifest);
            return manifest;
        }

        /// <inheritdoc />
        public LoadedPluginInfo? GetLoadedPlugin (string pluginId)
        {
            _loadedPlugins.TryGetValue(pluginId , out var info);
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
            // 若已加载则返回缓存
            if (_loadedPackages.TryGetValue(packageId , out var cached))
                return cached;

            var packageDir = Path.Combine(_pluginsPath , packageId);
            if (!Directory.Exists(packageDir))
                return null;

            var newManifestPath = Path.Combine(packageDir , "plugins-manifest.json");
            var oldManifestPath = Path.Combine(packageDir , "plugin.manifest.json");

            if (File.Exists(newManifestPath))
            {
                await LoadNewFormatPackage(packageDir , newManifestPath , null , ct);
                _loadedPackageDirs.Add(packageDir);
                return _loadedPackages.GetValueOrDefault(packageId);
            }
            else if (File.Exists(oldManifestPath))
            {
                await LoadOldFormatPackage(packageDir , oldManifestPath , null , ct);
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

            // 1. 卸载该包所有策略
            await UnloadSinglePackageInternal(pkgInfo);

            // 2. 从各字典移除
            RemovePackageFromDictionaries(packageId);

            // 3. 重新加载
            var packageDir = pkgInfo.PackagePath;
            var newManifestPath = Path.Combine(packageDir , "plugins-manifest.json");
            var oldManifestPath = Path.Combine(packageDir , "plugin.manifest.json");

            if (File.Exists(newManifestPath))
                await LoadNewFormatPackage(packageDir , newManifestPath , null , ct);
            else if (File.Exists(oldManifestPath))
                await LoadOldFormatPackage(packageDir , oldManifestPath , null , ct);

            // 重新跟踪该目录，避免 LoadPluginsAsync 重复扫描
            _loadedPackageDirs.Add(packageDir);
        }

        /// <inheritdoc />
        public async Task UnloadPackageAsync (string packageId)
        {
            if (!_loadedPackages.TryGetValue(packageId , out var pkgInfo))
                return;

            await UnloadSinglePackageInternal(pkgInfo);
            RemovePackageFromDictionaries(packageId);

            // 如果包目录存在，也删除文件
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

            if (pkgInfo.IsLegacyFormat)
                throw new InvalidOperationException($"旧格式插件包 {packageId} 不支持包级启用/禁用，请使用 SetStrategyEnabledAsync");

            var enables = pkgInfo.Enables ?? await LoadEnablesAsync(packageId , ct);
            enables.Enabled = enabled;
            pkgInfo.Enables = enables;

            await SaveEnablesAsync(packageId , enables , ct);

            // 立即同步到运行时
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

            if (pkg.IsLegacyFormat)
            {
                // 旧格式：写回 plugin.manifest.json
                var manifestPath = Path.Combine(pkg.PackagePath , "plugin.manifest.json");
                if (!File.Exists(manifestPath)) return;

#pragma warning disable CS0618 // 旧格式兼容
                var json = await File.ReadAllTextAsync(manifestPath , ct);
                var manifest = JsonSerializer.Deserialize<PluginManifest>(json ,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (manifest != null)
                {
                    manifest.Enabled = enabled;
                    var serialized = JsonSerializer.Serialize(manifest ,
                        new JsonSerializerOptions { WriteIndented = true , PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    await File.WriteAllTextAsync(manifestPath , serialized , ct);
                }
#pragma warning restore CS0618
            }
            else
            {
                // 新格式：写回 data/enables.json
                var enables = pkg.Enables ?? await LoadEnablesAsync(pkg.PackageManifest.Id , ct);
                enables.Strategies[strategyId] = enabled;
                pkg.Enables = enables;
                await SaveEnablesAsync(pkg.PackageManifest.Id , enables , ct);
            }
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

            // ZIP 炸弹安全验证（在解压前检查压缩比和总大小）
            ValidateZipSafety(packagePath);

            var tempDir = Path.Combine(Path.GetTempPath() , $"apair_plugin_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                // 解压到临时目录（使用 Copy+Delete 替代 Move，避免跨文件系统问题）
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

                // 检测格式 → 读取包 ID
                string packageId;
                bool isNewFormat;

                var newManifestPath = Path.Combine(tempDir , "plugins-manifest.json");
                var oldManifestPath = Path.Combine(tempDir , "plugin.manifest.json");

                if (File.Exists(newManifestPath))
                {
                    isNewFormat = true;
                    var json = await File.ReadAllTextAsync(newManifestPath , ct);
                    var manifest = JsonSerializer.Deserialize<PluginPackageManifest>(json ,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (manifest is null || string.IsNullOrEmpty(manifest.Id))
                        throw new InvalidDataException("plugins-manifest.json 格式无效：缺少 id 字段");
                    packageId = manifest.Id;
                }
                else if (File.Exists(oldManifestPath))
                {
                    isNewFormat = false;
                    var json = await File.ReadAllTextAsync(oldManifestPath , ct);
#pragma warning disable CS0618
                    var manifest = JsonSerializer.Deserialize<PluginManifest>(json ,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
#pragma warning restore CS0618
                    if (manifest is null || string.IsNullOrEmpty(manifest.Id))
                        throw new InvalidDataException("plugin.manifest.json 格式无效：缺少 id 字段");
                    packageId = manifest.Id;
                }
                else
                {
                    throw new InvalidDataException("插件包内缺少 plugins-manifest.json 或 plugin.manifest.json 文件");
                }

                ct.ThrowIfCancellationRequested();

                // 目标目录
                var targetDir = Path.Combine(_pluginsPath , packageId);
                if (Directory.Exists(targetDir))
                    throw new InvalidDataException($"插件包 \"{packageId}\" 已存在，请先卸载后再安装");

                Directory.CreateDirectory(targetDir);

                // 复制所有文件到目标目录
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

                // 新格式：自动创建 data/enables.json（默认全部启用）
                if (isNewFormat)
                {
                    var enables = new PluginEnables { Enabled = true };
                    await SaveEnablesAsync(packageId , enables , ct);
                }

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
            // Dispose 所有生命周期插件
            foreach (var (_, pkgInfo) in _loadedPackages)
            {
                await UnloadSinglePackageInternal(pkgInfo);
            }

            _loadedPackages.Clear();
            _strategyToPackage.Clear();
            _strategyEntryMap.Clear();
            _loadedPlugins.Clear();
            _loadedManifests.Clear();
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
        /// 同步旧字典（_loadedManifests, _loadedPlugins）以保持向后兼容。
        /// 在包加载完成后调用。
        /// </summary>
        private void SyncLegacyDictionaries ()
        {
            _loadedPlugins.Clear();
            _loadedManifests.Clear();

            foreach (var (_, pkgInfo) in _loadedPackages)
            {
                foreach (var (strategyId , pluginInfo) in pkgInfo.Strategies)
                {
                    _loadedPlugins[strategyId] = pluginInfo;

                    // 旧格式：PluginManifest 直接来自 manifest 文件
                    if (pkgInfo.IsLegacyFormat)
                    {
#pragma warning disable CS0618
                        var legacyManifest = new PluginManifest
                        {
                            Id = strategyId ,
                            Name = pluginInfo.Strategy.Name ,
                            Version = pkgInfo.PackageManifest.Version ,
                            Category = pkgInfo.PackageManifest.Type ,
                            Description = pkgInfo.PackageManifest.Description ,
                            Author = pkgInfo.PackageManifest.Author ,
                            Priority = pluginInfo.Strategy.Priority ,
                            Enabled = pluginInfo.Strategy.IsEnabled
                        };
                        _loadedManifests[strategyId] = legacyManifest;
#pragma warning restore CS0618
                    }
                }
            }
        }

        /// <summary>
        /// 加载新格式插件包（<c>plugins-manifest.json</c> + 策略 <c>manifest.json</c>）。
        /// </summary>
        private async Task<List<LoadedPluginInfo>> LoadNewFormatPackage (
            string packageDir , string manifestPath , string? category , CancellationToken ct)
        {
            var results = new List<LoadedPluginInfo>();

            var manifestJson = await File.ReadAllTextAsync(manifestPath , ct);
            var packageManifest = JsonSerializer.Deserialize<PluginPackageManifest>(manifestJson ,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (packageManifest == null || string.IsNullOrEmpty(packageManifest.Id))
                return results;

            // 类别过滤
            if (category != null && !string.Equals(packageManifest.Type , category , StringComparison.OrdinalIgnoreCase))
                return results;

            var packageId = packageManifest.Id;

            // 读取或创建 enables
            var enables = await LoadEnablesAsync(packageId , ct);
            if (string.IsNullOrEmpty(enables.Type))
                enables.Type = packageManifest.Type;

            var pkgInfo = new LoadedPackageInfo
            {
                PackageManifest = packageManifest ,
                PackagePath = packageDir ,
                Enables = enables ,
                IsLegacyFormat = false
            };

            foreach (var entry in packageManifest.Strategies)
            {
                ct.ThrowIfCancellationRequested();

                // 读取策略 manifest.json
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

                // 加载策略实例
                var strategy = await LoadStrategyFromEntry(entry , strategyManifest , packageDir , ct);
                if (strategy == null)
                    continue;

                // 应用优先级（来自 strategy manifest）
                strategy.Priority = strategyManifest.DefaultPriority;

                // 应用启用状态
                var isEnabled = enables.Enabled && enables.Strategies.GetValueOrDefault(strategyManifest.Id , true);
                strategy.IsEnabled = isEnabled;

                // 生命周期初始化
                if (strategy is IPluginLifecycle lifecycle)
                {
                    var host = new PluginHost(_pluginsPath , packageDir);
                    await lifecycle.InitializeAsync(host , ct);
                }

                var pluginInfo = new LoadedPluginInfo
                {
                    Manifest = null!, // 新格式无 PluginManifest
                    Strategy = strategy ,
                    PluginPath = packageDir ,
                    Entry = entry
                };

                pkgInfo.Strategies[strategyManifest.Id] = pluginInfo;
                _strategyToPackage[strategyManifest.Id] = packageId;
                _strategyEntryMap[strategyManifest.Id] = entry;
                results.Add(pluginInfo);

                _logger.LogInformation("加载插件策略：{StrategyId}（包：{PkgId}，类型：{LoadKind}）" ,
                    strategyManifest.Id , packageId ,
                    !string.IsNullOrEmpty(entry.Assembly) ? "assembly" :
                    !string.IsNullOrEmpty(entry.ScriptFile) ? entry.ScriptType ?? "script" : "unknown");
            }

            _loadedPackages[packageId] = pkgInfo;
            SyncLegacyDictionaries();
            return results;
        }

        /// <summary>
        /// 加载旧格式插件包（<c>plugin.manifest.json</c>），自动构造虚拟包。
        /// </summary>
        private async Task<LoadedPluginInfo?> LoadOldFormatPackage (
            string pluginDir , string manifestPath , string? category , CancellationToken ct)
        {
#pragma warning disable CS0618
            var manifestJson = await File.ReadAllTextAsync(manifestPath , ct);
            var manifest = JsonSerializer.Deserialize<PluginManifest>(manifestJson ,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest == null || string.IsNullOrEmpty(manifest.Id))
                return null;

            // 类别过滤
            if (category != null && !string.Equals(manifest.Category , category , StringComparison.OrdinalIgnoreCase))
                return null;

            var (packageManifest , _) = manifest.ToPackageEntry();
            var packageId = manifest.Id; // 旧格式：包 ID = 策略 ID

            var pkgInfo = new LoadedPackageInfo
            {
                PackageManifest = packageManifest ,
                PackagePath = pluginDir ,
                Enables = null , // 旧格式无 enables.json
                IsLegacyFormat = true
            };

            // 加载策略实例（复用现有逻辑）
            var strategy = await LoadPluginInternalLegacy(manifest , pluginDir , ct);
            if (strategy == null)
                return null;

            strategy.Priority = manifest.Priority;
            strategy.IsEnabled = manifest.Enabled;

            if (strategy is IPluginLifecycle lifecycle)
            {
                var host = new PluginHost(_pluginsPath , pluginDir);
                await lifecycle.InitializeAsync(host , ct);
            }

            var pluginInfo = new LoadedPluginInfo
            {
                Manifest = manifest ,
                Strategy = strategy ,
                PluginPath = pluginDir
            };

            pkgInfo.Strategies[manifest.Id] = pluginInfo;
            _strategyToPackage[manifest.Id] = packageId;
            _loadedPackages[packageId] = pkgInfo;

            SyncLegacyDictionaries();

            _logger.LogInformation("加载旧格式插件策略：{StrategyId}（虚拟包）" , manifest.Id);
            return pluginInfo;
#pragma warning restore CS0618
        }

        /// <summary>
        /// 从 PluginStrategyEntry 和 StrategyManifest 加载策略实例。
        /// </summary>
        private async Task<IPluginSeatingStrategy?> LoadStrategyFromEntry (
            PluginStrategyEntry entry , StrategyManifest strategyManifest , string packageDir , CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(entry.ScriptFile) && !string.IsNullOrEmpty(entry.ScriptType))
            {
                // 脚本插件
                var scriptPath = Path.Combine(packageDir , entry.Path , entry.ScriptFile);
                if (!File.Exists(scriptPath))
                {
                    // 尝试从包根目录查找
                    scriptPath = Path.Combine(packageDir , entry.ScriptFile);
                    if (!File.Exists(scriptPath))
                    {
                        _logger.LogWarning("脚本文件不存在：{ScriptFile}" , entry.ScriptFile);
                        return null;
                    }
                }

                var scriptCode = await File.ReadAllTextAsync(scriptPath , ct);

                if (_scriptAdapters.TryGetValue(entry.ScriptType , out var factory))
                {
#pragma warning disable CS0618
                    // 构造临时 PluginManifest 供适配器使用
                    var tempManifest = new PluginManifest
                    {
                        Id = strategyManifest.Id ,
                        Name = strategyManifest.DisplayName ,
                        Priority = strategyManifest.DefaultPriority ,
                        Enabled = true
                    };
                    return factory(scriptCode , tempManifest);
#pragma warning restore CS0618
                }

                _logger.LogWarning("未找到脚本适配器：{ScriptType}，策略：{StrategyId}" , entry.ScriptType , strategyManifest.Id);
                return null;
            }
            else if (!string.IsNullOrEmpty(entry.Assembly) && !string.IsNullOrEmpty(entry.EntryType))
            {
                // 程序集插件
                var assemblyPath = Path.Combine(packageDir , entry.Path , entry.Assembly);
                if (!File.Exists(assemblyPath))
                {
                    // 尝试从包根目录查找
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
        /// 旧格式加载内部策略实例（复用原有逻辑）。
        /// </summary>
#pragma warning disable CS0618
        private async Task<IPluginSeatingStrategy?> LoadPluginInternalLegacy (
            PluginManifest manifest , string pluginDir , CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(manifest.ScriptFile))
                return await LoadScriptPluginLegacy(manifest , pluginDir , ct);
            else if (!string.IsNullOrEmpty(manifest.Assembly))
                return LoadAssemblyPluginLegacy(manifest , pluginDir);

            return null;
        }
#pragma warning restore CS0618

        private IPluginSeatingStrategy? LoadAssemblyPluginLegacy (PluginManifest manifest , string pluginDir)
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

        private async Task<IPluginSeatingStrategy?> LoadScriptPluginLegacy (
            PluginManifest manifest , string pluginDir , CancellationToken ct)
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

        /// <summary>
        /// 卸载单个包内的所有策略（dispose lifecycle + unload contexts）。
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
            const long maxUncompressedSize = 500L * 1024 * 1024; // 500 MB
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
                    _loadedPlugins.Remove(strategyId);
                    _loadedManifests.Remove(strategyId);
                }
                _loadedPackages.Remove(packageId);
            }
        }
    }

    /// <summary>
    /// 表示已加载的插件信息，包含清单、策略实例和路径。
    /// </summary>
    public class LoadedPluginInfo
    {
        /// <summary>
        /// 旧格式插件清单。新格式插件此字段为 <c>null</c>。
        /// </summary>
        [Obsolete("新格式插件不使用 PluginManifest。使用 PluginManager.FindStrategy 获取包级信息。")]
        public PluginManifest Manifest { get; set; } = default!;

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
        /// 新格式策略对应的加载条目（来自 <c>plugins-manifest.json</c> 的 <c>strategies[]</c>）。
        /// 旧格式插件此字段为 <c>null</c>。
        /// </summary>
        public PluginStrategyEntry? Entry { get; set; }
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
