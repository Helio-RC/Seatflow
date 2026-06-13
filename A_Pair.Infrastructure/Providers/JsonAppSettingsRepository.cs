using System.Text.Json;
using System.Text.Json.Nodes;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using A_Pair.Infrastructure.Migration;
using A_Pair.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Infrastructure.Providers
{
    public class JsonAppSettingsRepository : IAppSettingsRepository
    {
        private readonly string _filePath;
        private readonly FileMigrationService _migration;
        private readonly ILogger<JsonAppSettingsRepository> _logger;

        public JsonAppSettingsRepository (
            string filePath ,
            FileMigrationService migration ,
            ILogger<JsonAppSettingsRepository>? logger = null)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _migration = migration ?? throw new ArgumentNullException(nameof(migration));
            _logger = logger ?? NullLogger<JsonAppSettingsRepository>.Instance;
        }

        public string SettingsFilePath => _filePath;

        public async Task<AppSettings> LoadAsync (CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogDebug("AppSettings 文件不存在，使用默认设置：{Path}" , _filePath);
                return new AppSettings();
            }

            var json = await File.ReadAllTextAsync(_filePath , cancellationToken);
            var node = JsonNode.Parse(json);
            if (node is not null)
            {
                var fileVersion = node["version"]?.GetValue<string>() ?? "1.0";
                node = _migration.Migrate("appSettings" , node , fileVersion , FileVersionInfo.GetCurrentVersion("appSettings"));
                json = node.ToJsonString();
            }
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings is null)
            {
                _logger.LogWarning("AppSettings 反序列化结果为 null：{Path}" , _filePath);
                return new AppSettings();
            }
            _logger.LogInformation("AppSettings 已加载：{Path}" , _filePath);
            return settings;
        }

        public async Task SaveAsync (AppSettings settings , CancellationToken cancellationToken = default)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            settings.Version = FileVersionInfo.GetCurrentVersion("appSettings");
            var options = JsonOptions.WriteIndented;
            var json = JsonSerializer.Serialize(settings , options);
            await File.WriteAllTextAsync(_filePath , json , cancellationToken);
            _logger.LogInformation("AppSettings 已保存：{Path}" , _filePath);
        }
    }
}