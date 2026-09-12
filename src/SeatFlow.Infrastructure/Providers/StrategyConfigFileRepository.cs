using System.Text.Json;
using System.Text.Json.Nodes;
using SeatFlow.Core.Models;
using SeatFlow.Core.Storage;
using SeatFlow.Infrastructure.Migration;
using SeatFlow.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Providers
{
    /// <summary>
    /// 策略运行时配置仓储（存储抽象版本）。
    /// </summary>
    public class StrategyConfigFileRepository
    {
        private readonly ILocalDataStore _store;
        private readonly string _configDir;
        private readonly FileMigrationService _migration;
        private readonly ILogger<StrategyConfigFileRepository> _logger;

        /// <param name="configDir">配置目录（相对存储根，如 <c>StrategyConfig</c>）。</param>
        /// <param name="migration">文件版本迁移服务。</param>
        /// <param name="logger">日志记录器。</param>
        public StrategyConfigFileRepository(
            ILocalDataStore store,
            string configDir,
            FileMigrationService migration,
            ILogger<StrategyConfigFileRepository>? logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _configDir = NormalizeDir(configDir);
            _migration = migration ?? throw new ArgumentNullException(nameof(migration));
            _logger = logger ?? NullLogger<StrategyConfigFileRepository>.Instance;
        }

        /// <summary>兼容构造：直接指定文件系统目录。</summary>
        public StrategyConfigFileRepository(
            string configDir,
            FileMigrationService migration,
            ILogger<StrategyConfigFileRepository>? logger = null)
            : this(new FileSystemDataStore(configDir), "", migration, logger)
        {
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public async Task<StrategyConfig?> LoadAsync(string strategyId, CancellationToken ct = default)
        {
            var filePath = GetFilePath(strategyId);
            var json = await _store.ReadTextAsync(filePath, ct);
            if (json is null)
                return null;

            var config = DeserializeWithMigration(json);
            _logger.LogInformation("策略配置已加载: {StrategyId}", strategyId);
            return config;
        }

        public async Task SaveAsync(string strategyId, StrategyConfig config, CancellationToken ct = default)
        {
            config.Version = FileVersionInfo.GetCurrentVersion("strategyConfig");
            var filePath = GetFilePath(strategyId);
            var json = JsonSerializer.Serialize(config, JsonOptions);
            await _store.WriteTextAsync(filePath, json, ct);
            _logger.LogInformation("策略配置已保存：{Id} → {Path}", strategyId, filePath);
        }

        public async Task<Dictionary<string, StrategyConfig>> LoadAllAsync(CancellationToken ct = default)
        {
            var results = new Dictionary<string, StrategyConfig>();
            foreach (var filePath in await _store.ListAsync(_configDir, "*.config.json", ct))
            {
                var name = filePath[(filePath.LastIndexOf('/') + 1)..];
                var fileName = Path.GetFileNameWithoutExtension(name);
                var strategyId = Path.GetFileNameWithoutExtension(fileName);

                var json = await _store.ReadTextAsync(filePath, ct);
                if (json is null) continue;
                var config = DeserializeWithMigration(json);
                if (config is not null)
                    results[strategyId] = config;
            }

            _logger.LogDebug("已加载 {Count} 个策略配置", results.Count);
            return results;
        }

        private StrategyConfig? DeserializeWithMigration(string json)
        {
            var node = JsonNode.Parse(json);
            if (node is not null)
            {
                var fileVersion = node["version"]?.GetValue<string>() ?? "1.0";
                node = _migration.Migrate("strategyConfig", node, fileVersion, FileVersionInfo.GetCurrentVersion("strategyConfig"));
                json = node.ToJsonString();
            }
            return JsonSerializer.Deserialize<StrategyConfig>(json, JsonOptions);
        }

        private string GetFilePath(string strategyId)
        {
            if (strategyId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || strategyId.Contains(Path.DirectorySeparatorChar)
                || strategyId.Contains(Path.AltDirectorySeparatorChar)
                || strategyId.Contains('/'))
                throw new ArgumentException($"策略 ID 含非法字符: {strategyId}");
            return (_configDir.Length == 0 ? "" : _configDir + "/") + $"{strategyId}.config.json";
        }

        private static string NormalizeDir(string dir)
            => (dir ?? "").Trim().Trim('/', '\\');
    }
}
