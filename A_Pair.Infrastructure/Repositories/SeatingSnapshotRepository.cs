using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using A_Pair.Core.Models;

namespace A_Pair.Infrastructure.Repositories
{
    public class SeatingSnapshotRepository
    {
        private readonly string _basePath;

        public SeatingSnapshotRepository(string basePath)
        {
            _basePath = basePath;
            Directory.CreateDirectory(_basePath);
        }

        public async Task SaveAsync(SeatingSnapshot snapshot)
        {
            var path = Path.Combine(_basePath, snapshot.Id + ".json");
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }

        public SeatingSnapshot? Load(string id)
        {
            var path = Path.Combine(_basePath, id + ".json");
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SeatingSnapshot>(json);
        }
    }
}
