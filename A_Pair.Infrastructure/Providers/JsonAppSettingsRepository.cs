using System.Text.Json;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;

namespace A_Pair.Infrastructure.Providers
{
    /// <summary>
    /// JSON 格式的应用程序设置仓储，将 <see cref="AppSettings"/> 以 JSON 格式持久化到本地文件。
    /// </summary>
    /// <remarks>
    /// 如果配置文件不存在，<see cref="LoadAsync"/> 返回默认的 <see cref="AppSettings"/> 实例。
    /// 保存时自动创建目录结构。
    /// </remarks>
    /// <param name="filePath">配置文件的完整路径。</param>
    public class JsonAppSettingsRepository (string filePath) : IAppSettingsRepository
    {
        private readonly string _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

        /// <summary>
        /// 获取配置文件的完整路径。
        /// </summary>
        public string SettingsFilePath => _filePath;

        /// <inheritdoc />
        public async Task<AppSettings> LoadAsync (CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_filePath))
                return new AppSettings();

            var json = await File.ReadAllTextAsync(_filePath , cancellationToken);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }

        /// <inheritdoc />
        public async Task SaveAsync (AppSettings settings , CancellationToken cancellationToken = default)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings , options);
            await File.WriteAllTextAsync(_filePath , json , cancellationToken);
        }
    }
}