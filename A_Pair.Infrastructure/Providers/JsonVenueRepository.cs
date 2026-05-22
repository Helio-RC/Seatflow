using System.Text.Json;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using A_Pair.Infrastructure.Serialization;

namespace A_Pair.Infrastructure.Providers
{
    /// <summary>
    /// JSON 格式的会场（场地）仓储，将 <see cref="ClassroomLayoutDefinition"/> 以 JSON 格式持久化到本地文件。
    /// </summary>
    /// <remarks>
    /// 每个会场保存为独立的 <c>*.venue.json</c> 文件，文件名格式为 <c><venueId>.venue.json</c>。
    /// 使用 <see cref="SeatJsonConverter"/> 支持 <see cref="Seat"/> 派生类的多态序列化。
    /// </remarks>
    /// <param name="venuesFolder">会场文件存储目录。</param>
    public class JsonVenueRepository : IVenueRepository
    {
        private readonly string _venuesFolder;

        /// <summary>
        /// 初始化 JSON 会场仓储，确保存储目录存在。
        /// </summary>
        /// <param name="venuesFolder">会场文件存储目录。</param>
        public JsonVenueRepository (string venuesFolder)
        {
            _venuesFolder = venuesFolder ?? throw new ArgumentNullException(nameof(venuesFolder));
            Directory.CreateDirectory(_venuesFolder);
        }

        /// <inheritdoc />
        public async Task SaveAsync (string venueId , ClassroomLayoutDefinition layout , CancellationToken cancellationToken = default)
        {
            var filePath = GetFilePath(venueId);
            var venueFile = new VenueFile
            {
                Version = "1.0" ,
                VenueId = venueId ,
                Layout = layout
            };
            var options = SerializerOptions;
            var json = JsonSerializer.Serialize(venueFile , options);
            await File.WriteAllTextAsync(filePath , json , cancellationToken);
        }

        /// <inheritdoc />
        public async Task<ClassroomLayoutDefinition?> LoadAsync (string venueId , CancellationToken cancellationToken = default)
        {
            var filePath = GetFilePath(venueId);
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath , cancellationToken);
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
        private string GetFilePath(string venueId)
        {
            if (venueId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || venueId.Contains(Path.DirectorySeparatorChar)
                || venueId.Contains(Path.AltDirectorySeparatorChar))
                throw new ArgumentException($"会场 ID 含非法字符: {venueId}");
            return Path.Combine(_venuesFolder, $"{venueId}.venue.json");
        }

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        static JsonVenueRepository()
        {
            SerializerOptions.Converters.Add(new SeatJsonConverter());
        }
    }
}