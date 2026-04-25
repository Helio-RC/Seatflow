using System.Text.Json;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;

namespace A_Pair.Infrastructure.Providers
{
    public class JsonAppSettingsRepository (string filePath) : IAppSettingsRepository
    {
        private readonly string _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

        public string SettingsFilePath => _filePath;

        public async Task<AppSettings> LoadAsync (CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_filePath))
                return new AppSettings();

            var json = await File.ReadAllTextAsync(_filePath , cancellationToken);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }

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