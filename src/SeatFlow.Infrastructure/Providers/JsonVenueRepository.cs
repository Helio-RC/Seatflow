using System.Text.Json;
using System.Text.Json.Nodes;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Core.Storage;
using SeatFlow.Infrastructure.Migration;
using SeatFlow.Infrastructure.Serialization;
using SeatFlow.Infrastructure.Storage;
using SeatFlow.Infrastructure.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Providers
{
    /// <summary>
    /// JSON 格式的会场（场地）仓储，将 <see cref="ClassroomLayoutDefinition"/> 以 JSON 格式持久化到本地。
    /// </summary>
    /// <remarks>
    /// 每个会场保存为独立的 <c>*.venue.json</c> 文件，文件名格式为 <c><venueId>.venue.json</c>。
    /// 使用 <see cref="SeatJsonConverter"/> 支持 <see cref="Seat"/> 派生类的多态序列化。
    /// 存储通过 <see cref="ILocalDataStore"/> 抽象：桌面 = 文件系统，WASM = IndexedDB。
    /// </remarks>
    public class JsonVenueRepository : IVenueRepository
    {
        private readonly ILocalDataStore _store;
        private readonly string _venuesDir;
        private readonly FileMigrationService _migration;
        private readonly ILogger<JsonVenueRepository> _logger;

        /// <summary>
        /// 初始化 JSON 会场仓储（存储抽象版本）。
        /// </summary>
        /// <param name="store">数据存储实现。</param>
        /// <param name="venuesDir">会场目录（相对存储根，如 <c>Venues</c>）。</param>
        /// <param name="migration">文件迁移服务。</param>
        /// <param name="logger">日志记录器。</param>
        public JsonVenueRepository (
            ILocalDataStore store ,
            string venuesDir ,
            FileMigrationService migration ,
            ILogger<JsonVenueRepository>? logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _venuesDir = NormalizeDir(venuesDir);
            _migration = migration ?? throw new ArgumentNullException(nameof(migration));
            _logger = logger ?? NullLogger<JsonVenueRepository>.Instance;
            _ = store.EnsureDirAsync(_venuesDir);
        }

        /// <summary>
        /// 初始化 JSON 会场仓储（兼容构造：直接指定文件系统目录）。
        /// </summary>
        public JsonVenueRepository (
            string venuesFolder ,
            FileMigrationService migration ,
            ILogger<JsonVenueRepository>? logger = null)
            : this(new FileSystemDataStore(venuesFolder) ,
                  "" ,
                  migration ,
                  logger)
        {
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
            // 首次序列化（不含哈希）用于计算内容哈希
            var json = JsonSerializer.Serialize(venueFile , options);
            venueFile.ContentHash = ContentHashHelper.ComputeSha256(json);
            // 二次序列化（含哈希）用于保存
            json = JsonSerializer.Serialize(venueFile , options);
            await _store.WriteTextAsync(filePath , json , cancellationToken);
            _logger.LogInformation("会场已保存：{VenueId} → {Path}" , venueId , filePath);
        }

        /// <inheritdoc />
        public async Task<ClassroomLayoutDefinition?> LoadAsync (string venueId , CancellationToken cancellationToken = default)
        {
            var filePath = GetFilePath(venueId);
            var json = await _store.ReadTextAsync(filePath , cancellationToken);
            if (json is null)
                return null;

            var node = JsonNode.Parse(json);
            if (node is not null)
            {
                var fileVersion = node["version"]?.GetValue<string>() ?? "1.0";
                node = _migration.Migrate("venue" , node , fileVersion , FileVersionInfo.GetCurrentVersion("venue"));
                json = node.ToJsonString();
            }
            var options = SerializerOptions;
            var venueFile = JsonSerializer.Deserialize<VenueFile>(json , options);
            _logger.LogInformation("场馆已加载: {VenueId}" , venueId);
            return venueFile?.Layout;
        }

        /// <inheritdoc />
        public async Task<string?> GetContentHashAsync (string venueId , CancellationToken ct = default)
        {
            var filePath = GetFilePath(venueId);
            var json = await _store.ReadTextAsync(filePath , ct);
            if (json is null) return null;
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("contentHash" , out var h) ? h.GetString() : null;
        }

        /// <inheritdoc />
        public async Task<string?> GetRawVenueFileAsync (string venueId , CancellationToken ct = default)
        {
            return await _store.ReadTextAsync(GetFilePath(venueId) , ct);
        }

        /// <inheritdoc />
        public async Task DeleteAsync (string venueId , CancellationToken cancellationToken = default)
        {
            var filePath = GetFilePath(venueId);
            if (await _store.ExistsAsync(filePath , cancellationToken))
                await _store.DeleteAsync(filePath , cancellationToken);
            _logger.LogInformation("场馆已删除: {VenueId}" , venueId);
        }

        /// <inheritdoc />
        public async Task<IEnumerable<string>> ListVenueIdsAsync (CancellationToken cancellationToken = default)
        {
            var files = await _store.ListAsync(_venuesDir , "*.venue.json" , cancellationToken);
            var ids = files.Select(f =>
            {
                var name = f[(f.LastIndexOf('/') + 1)..];
                return Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(name));
            });
            _logger.LogDebug("列出 {Count} 个场馆 ID" , ids.Count());
            return ids;
        }

        /// <summary>
        /// 获取指定会场的存储相对路径。
        /// </summary>
        private string GetFilePath (string venueId)
        {
            if (venueId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || venueId.Contains(Path.DirectorySeparatorChar)
                || venueId.Contains(Path.AltDirectorySeparatorChar)
                || venueId.Contains('/'))
                throw new ArgumentException($"会场 ID 含非法字符: {venueId}");
            return (_venuesDir.Length == 0 ? "" : _venuesDir + "/") + $"{venueId}.venue.json";
        }

        private static string NormalizeDir (string dir)
            => (dir ?? "").Trim().Trim('/', '\\');

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