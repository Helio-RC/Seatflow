using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using A_Pair.Infrastructure.Serialization;

namespace A_Pair.Infrastructure.Providers
{
    public class JsonVenueRepository : IVenueRepository
    {
        private readonly string _venuesFolder;

        public JsonVenueRepository (string venuesFolder)
        {
            _venuesFolder = venuesFolder ?? throw new ArgumentNullException(nameof(venuesFolder));
            Directory.CreateDirectory(_venuesFolder);
        }

        public async Task SaveAsync (string venueId , ClassroomLayoutDefinition layout , CancellationToken cancellationToken = default)
        {
            var filePath = GetFilePath(venueId);
            var venueFile = new VenueFile
            {
                Version = "1.0" ,
                VenueId = venueId ,
                Layout = layout
            };
            var options = GetSerializerOptions();
            var json = JsonSerializer.Serialize(venueFile , options);
            await File.WriteAllTextAsync(filePath , json , cancellationToken);
        }

        public async Task<ClassroomLayoutDefinition?> LoadAsync (string venueId , CancellationToken cancellationToken = default)
        {
            var filePath = GetFilePath(venueId);
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath , cancellationToken);
            var options = GetSerializerOptions();
            var venueFile = JsonSerializer.Deserialize<VenueFile>(json , options);
            return venueFile?.Layout;
        }

        public Task<IEnumerable<string>> ListVenueIdsAsync (CancellationToken cancellationToken = default)
        {
            var files = Directory.GetFiles(_venuesFolder , "*.venue.json");
            var ids = files.Select(f => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(f)));
            return Task.FromResult(ids);
        }

        private string GetFilePath (string venueId) => Path.Combine(_venuesFolder , $"{venueId}.venue.json");

        /// <summary>
        /// 获取包含多态类型支持的序列化选项
        /// </summary>
        private static JsonSerializerOptions GetSerializerOptions ()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true ,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            options.Converters.Add(new SeatJsonConverter());
            // 可在此添加其他自定义转换器
            return options;
        }
    }
}