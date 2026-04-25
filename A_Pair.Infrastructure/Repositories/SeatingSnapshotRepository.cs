using System.Text.Json;
using A_Pair.Core.Models;

namespace A_Pair.Infrastructure.Repositories
{
    public class SeatingSnapshotRepository
    {
        private readonly string _basePath;

        public SeatingSnapshotRepository (string basePath)
        {
            _basePath = basePath;
            Directory.CreateDirectory(_basePath);
        }

        public async Task SaveAsync (SeatingSnapshot snapshot)
        {
            var path = Path.Combine(_basePath , snapshot.Id + ".json");
            var json = JsonSerializer.Serialize(snapshot , new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path , json);
        }

        public SeatingSnapshot? Load (string id)
        {
            var path = Path.Combine(_basePath , id + ".json");
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SeatingSnapshot>(json);
        }

        public async Task<IReadOnlyList<SeatingSnapshot>> ListByVenueAsync (string venueId)
        {
            var snapshots = new List<SeatingSnapshot>();
            var files = Directory.EnumerateFiles(_basePath , "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var snapshot = JsonSerializer.Deserialize<SeatingSnapshot>(json);
                    if (snapshot != null && snapshot.LayoutId == venueId)
                    {
                        snapshots.Add(snapshot);
                    }
                }
                catch
                {
                    // ignore corrupt files
                }
            }
            return snapshots.OrderByDescending(s => s.CreatedAt).ToList();
        }

        public async Task<IReadOnlyList<SeatingSnapshot>> ListAllAsync ()
        {
            var snapshots = new List<SeatingSnapshot>();
            var files = Directory.EnumerateFiles(_basePath , "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var snapshot = JsonSerializer.Deserialize<SeatingSnapshot>(json);
                    if (snapshot != null)
                        snapshots.Add(snapshot);
                }
                catch { }
            }
            return snapshots.OrderByDescending(s => s.CreatedAt).ToList();
        }

        public Task DeleteAsync (string id)
        {
            var path = Path.Combine(_basePath , id + ".json");
            if (File.Exists(path))
                File.Delete(path);
            return Task.CompletedTask;
        }
    }
}