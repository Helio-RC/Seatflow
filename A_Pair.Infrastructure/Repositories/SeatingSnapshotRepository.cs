using System.Text.Json;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;

namespace A_Pair.Infrastructure.Repositories
{
    /// <summary>
    /// 座位快照仓储，按 <c>Assignments/{venueId}/{yyyyMMdd}/{snapshotId}.json</c> 组织存储。
    /// </summary>
    public class SeatingSnapshotRepository : ISeatingSnapshotRepository
    {
        private readonly string _basePath;

        public SeatingSnapshotRepository (string basePath)
        {
            _basePath = basePath;
        }

        private static string GetFilePath (string venueId, DateTime date, string id)
            => Path.Combine(venueId, date.ToString("yyyyMMdd"), id + ".json");

        /// <inheritdoc />
        public async Task SaveAsync (SeatingSnapshot snapshot)
        {
            var dir = Path.Combine(_basePath, snapshot.LayoutId, snapshot.CreatedAt.ToString("yyyyMMdd"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(_basePath, GetFilePath(snapshot.LayoutId, snapshot.CreatedAt, snapshot.Id));
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }

        /// <inheritdoc />
        public async Task SaveVenueInfoAsync (string venueId, VenueSnapshotInfo info)
        {
            var dir = Path.Combine(_basePath, venueId);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "_venue.json");
            var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }

        /// <inheritdoc />
        public SeatingSnapshot? Load (string id)
        {
            foreach (var venueDir in SafeEnumerateDirectories(_basePath))
            {
                foreach (var dateDir in SafeEnumerateDirectories(venueDir))
                {
                    var path = Path.Combine(dateDir, id + ".json");
                    if (File.Exists(path))
                        return JsonSerializer.Deserialize<SeatingSnapshot>(File.ReadAllText(path));
                }
            }
            return null;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<SeatingSnapshot>> ListByVenueAsync (string venueId)
        {
            var snapshots = LoadFromDir(Path.Combine(_basePath, venueId));
            return Task.FromResult<IReadOnlyList<SeatingSnapshot>>(
                snapshots.OrderByDescending(s => s.CreatedAt).ToList());
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<SeatingSnapshot>> ListAllAsync ()
        {
            var snapshots = new List<SeatingSnapshot>();
            foreach (var venueDir in SafeEnumerateDirectories(_basePath))
                snapshots.AddRange(LoadFromDir(venueDir));
            return Task.FromResult<IReadOnlyList<SeatingSnapshot>>(
                snapshots.OrderByDescending(s => s.CreatedAt).ToList());
        }

        /// <inheritdoc />
        public Task DeleteAsync (string id)
        {
            foreach (var venueDir in SafeEnumerateDirectories(_basePath))
            {
                foreach (var dateDir in SafeEnumerateDirectories(venueDir))
                {
                    var path = Path.Combine(dateDir, id + ".json");
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        return Task.CompletedTask;
                    }
                }
            }
            return Task.CompletedTask;
        }

        private static List<SeatingSnapshot> LoadFromDir (string venueDir)
        {
            var snapshots = new List<SeatingSnapshot>();
            foreach (var dateDir in SafeEnumerateDirectories(venueDir))
            {
                foreach (var file in Directory.EnumerateFiles(dateDir, "*.json"))
                {
                    try
                    {
                        var snapshot = JsonSerializer.Deserialize<SeatingSnapshot>(File.ReadAllText(file));
                        if (snapshot is not null) snapshots.Add(snapshot);
                    }
                    catch { }
                }
            }
            return snapshots;
        }

        private static string[] SafeEnumerateDirectories (string path)
        {
            try { return Directory.GetDirectories(path); }
            catch { return []; }
        }
    }
}
