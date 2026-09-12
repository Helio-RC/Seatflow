using System.Text.Json;
using System.Text.Json.Nodes;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Core.Storage;
using SeatFlow.Infrastructure.Migration;
using SeatFlow.Infrastructure.Serialization;
using SeatFlow.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Providers
{
    public class JsonAppSettingsRepository : IAppSettingsRepository
    {
        private readonly ILocalDataStore _store;
        private readonly string _filePath;
        private readonly FileMigrationService _migration;
        private readonly ILogger<JsonAppSettingsRepository> _logger;

        /// <summary>
        /// 初始化 AppSettings 仓储（存储抽象版本）。
        /// </summary>
        /// <param name="store">数据存储实现。</param>
        /// <param name="relativeFilePath">AppSettings.json 相对存储路径（如 <c>AppSettings.json</c>）。</param>
        /// <param name="migration">文件迁移服务。</param>
        /// <param name="logger">日志记录器。</param>
        public JsonAppSettingsRepository(
            ILocalDataStore store,
            string relativeFilePath,
            FileMigrationService migration,
            ILogger<JsonAppSettingsRepository>? logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _filePath = NormalizePath(relativeFilePath) ?? throw new ArgumentNullException(nameof(relativeFilePath));
            _migration = migration ?? throw new ArgumentNullException(nameof(migration));
            _logger = logger ?? NullLogger<JsonAppSettingsRepository>.Instance;
        }

        /// <summary>
        /// 初始化 AppSettings 仓储（兼容构造：直接指定完整文件路径）。
        /// </summary>
        public JsonAppSettingsRepository(
            string filePath,
            FileMigrationService migration,
            ILogger<JsonAppSettingsRepository>? logger = null)
            : this(new FileSystemDataStore(Path.GetDirectoryName(filePath) ?? ""),
                  Path.GetFileName(filePath),
                  migration,
                  logger)
        {
            _desktopFilePath = filePath;
        }

        private readonly string? _desktopFilePath;

        public string SettingsFilePath => _desktopFilePath ?? _filePath;

        /// <inheritdoc />
        public Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
            => _store.ExistsAsync(_filePath, cancellationToken);

        public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            var json = await _store.ReadTextAsync(_filePath, cancellationToken);
            if (json is null)
            {
                _logger.LogDebug("AppSettings 文件不存在，使用默认设置：{Path}", _filePath);
                return new AppSettings();
            }

            var node = JsonNode.Parse(json);
            if (node is not null)
            {
                var fileVersion = node["version"]?.GetValue<string>() ?? "1.0";
                node = _migration.Migrate("appSettings", node, fileVersion, FileVersionInfo.GetCurrentVersion("appSettings"));
                json = node.ToJsonString();
            }
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings is null)
            {
                _logger.LogWarning("AppSettings 反序列化结果为 null：{Path}", _filePath);
                return new AppSettings();
            }
            _logger.LogInformation("AppSettings 已加载：{Path}", _filePath);
            return settings;
        }

        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            settings.Version = FileVersionInfo.GetCurrentVersion("appSettings");
            var options = JsonOptions.WriteIndented;
            var json = JsonSerializer.Serialize(settings, options);
            await _store.WriteTextAsync(_filePath, json, cancellationToken);
            _logger.LogInformation("AppSettings 已保存：{Path}", _filePath);
        }

        private static string NormalizePath(string path)
            => path?.Replace('\\', '/').TrimStart('/') ?? "";
    }
}