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
    /// <summary>
    /// JSON 格式的会场（场地）仓储，将 <see cref="ClassroomLayoutDefinition"/> 以 JSON 格式持久化到本地文件。
    /// </summary>
    /// <remarks>
    /// 每个会场保存为独立的 <c>*.venue.json</c> 文件，文件名格式为 <c><venueId>.venue.json</c>。
    /// 使用 <see cref="SeatJsonConverter"/> 支持 <see cref="Seat"/> 派生类的多态序列化。
    /// </remarks>
    public class JsonVenueRepository : IVenueRepository
    {
        private readonly string _venuesFolder;
        private readonly FileMigrationService _migration;
        private readonly ILogger<JsonVenueRepository> _logger;

        /// <summary>
        /// 初始化 JSON 会场仓储，确保存储目录存在。
        /// </summary>
        public JsonVenueRepository (
            string venuesFolder ,
            FileMigrationService migration ,
            ILogger<JsonVenueRepository>? logger = null)
        {
            _venuesFolder = venuesFolder ?? throw new ArgumentNullException(nameof(venuesFolder));
            _migration = migration ?? throw new ArgumentNullException(nameof(migration));
            _logger = logger ?? NullLogger<JsonVenueRepository>.Instance;
            Directory.CreateDirectory(_venuesFolder);
        }

        /// <inheritdoc />
        public async Task SaveAsync (string venueId , ClassroomLayoutDefinition layout , CancellationToken cancellationToken = default)
        {
            var filePath = GetFilePath(venueId);
            var venueFile = new VenueFile
            {
                Version = FileVersionInfo.GetCurrentVersion("venue") ,
                VenueId = venueId ,
                Layout = layout
            };
            var options = SerializerOptions;
            var json = JsonSerializer.Serialize(venueFile , options);
            await File.WriteAllTextAsync(filePath , json , cancellationToken);
            _logger.LogInformation("会场已保存：{VenueId} → {Path}" , venueId , filePath);
        }

        /// <inheritdoc />
        public async Task<ClassroomLayoutDefinition?> LoadAsync (string venueId , CancellationToken cancellationToken = default)
        {
            var filePath = GetFilePath(venueId);
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath , cancellationToken);
            var node = JsonNode.Parse(json);
            if (node is not null)
            {
                var fileVersion = node["version"]?.GetValue<string>() ?? "1.0";
                node = _migration.Migrate("venue" , node , fileVersion , FileVersionInfo.GetCurrentVersion("venue"));
                json = node.ToJsonString();
            }
            var options = SerializerOptions;
            var venueFile = JsonSerializer.Deserialize<VenueFile>(json , options);
            return venueFile?.Layout;
        }

        /// <inheritdoc />
        public Task DeleteAsync (string venueId , CancellationToken cancellationToken = default)
        {
            var filePath = GetFilePath(venueId);
            if (File.Exists(filePath))
                File.Delete(filePath);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IEnumerable<string>> ListVenueIdsAsync (CancellationToken cancellationToken = default)
        {
            var files = Directory.GetFiles(_venuesFolder , "*.venue.json");
            var ids = files.Select(f => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(f)));
            return Task.FromResult(ids);
        }

        /// <summary>
        /// 获取指定会场的文件路径。
        /// </summary>
        /// <param name="venueId">会场 ID。</param>
        /// <returns>会场文件的完整路径。</returns>
        private string GetFilePath (string venueId)
        {
            if (venueId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || venueId.Contains(Path.DirectorySeparatorChar)
                || venueId.Contains(Path.AltDirectorySeparatorChar))
                throw new ArgumentException($"会场 ID 含非法字符: {venueId}");
            return Path.Combine(_venuesFolder , $"{venueId}.venue.json");
        }

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true ,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        static JsonVenueRepository ()
        {
            SerializerOptions.Converters.Add(new SeatJsonConverter());
        }
    }
}