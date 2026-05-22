using System.Text.Json;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;

namespace A_Pair.Infrastructure.Repositories
{
    /// <summary>
    /// 座位快照仓储，按 <c>Assignments/{venueId}/{yyyyMMdd}/{snapshotId}.json</c> 组织存储。
    /// 维护内存索引实现 O(1) 的 Load/Delete 操作。
    /// </summary>
    public class SeatingSnapshotRepository : ISeatingSnapshotRepository
    {
        private readonly string _basePath;
        private readonly Dictionary<string, string> _index = []; // snapshot ID → full file path
        private bool _indexBuilt;

        public SeatingSnapshotRepository(string basePath)
        {
            _basePath = basePath;
        }

        private static string GetFilePath(string venueId, DateTime date, string id)
            => Path.Combine(venueId, date.ToString("yyyyMMdd"), id + ".json");

        private void BuildIndex()
        {
            if (_indexBuilt) return;
            foreach (var venueDir in SafeEnumerateDirectories(_basePath))
            {
                foreach (var dateDir in SafeEnumerateDirectories(venueDir))
                {
                    foreach (var file in Directory.EnumerateFiles(dateDir, "*.json"))
                    {
                        var id = Path.GetFileNameWithoutExtension(file);
                        if (!_index.ContainsKey(id))
                            _index[id] = file;
                    }
                }
            }
            _indexBuilt = true;
        }

        /// <inheritdoc />
        public async Task SaveAsync(SeatingSnapshot snapshot, CancellationToken ct = default)
        {
            var dir = Path.Combine(_basePath, snapshot.LayoutId, snapshot.CreatedAt.ToString("yyyyMMdd"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(_basePath, GetFilePath(snapshot.LayoutId, snapshot.CreatedAt, snapshot.Id));
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json, ct);
            _index[snapshot.Id] = path;
            _indexBuilt = true;
        }

        /// <inheritdoc />
        public async Task SaveVenueInfoAsync(string venueId, VenueSnapshotInfo info, CancellationToken ct = default)
        {
            var dir = Path.Combine(_basePath, venueId);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "_venue.json");
            var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json, ct);
        }

        /// <inheritdoc />
        public SeatingSnapshot? Load(string id)
        {
            BuildIndex();
            if (_index.TryGetValue(id, out var path) && File.Exists(path))
                return JsonSerializer.Deserialize<SeatingSnapshot>(File.ReadAllText(path));
            return null;
        }

        /// <inheritdoc />
        public async Task<SeatingSnapshot?> LoadAsync(string id, CancellationToken ct = default)
        {
            BuildIndex();
            if (_index.TryGetValue(id, out var path) && File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path, ct);
                return JsonSerializer.Deserialize<SeatingSnapshot>(json);
            }
            return null;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<SeatingSnapshot>> ListByVenueAsync(string venueId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var snapshots = LoadFromDir(Path.Combine(_basePath, venueId));
            return Task.FromResult<IReadOnlyList<SeatingSnapshot>>(
                snapshots.OrderByDescending(s => s.CreatedAt).ToList());
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<SeatingSnapshot>> ListAllAsync()
        {
            var snapshots = new List<SeatingSnapshot>();
            foreach (var venueDir in SafeEnumerateDirectories(_basePath))
                snapshots.AddRange(LoadFromDir(venueDir));
            return Task.FromResult<IReadOnlyList<SeatingSnapshot>>(
                snapshots.OrderByDescending(s => s.CreatedAt).ToList());
        }

        /// <inheritdoc />
        public Task DeleteAsync(string id, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            BuildIndex();
            if (_index.TryGetValue(id, out var path) && File.Exists(path))
            {
                File.Delete(path);
                _index.Remove(id);
            }
            return Task.CompletedTask;
        }

        private static List<SeatingSnapshot> LoadFromDir(string venueDir)
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

        private static string[] SafeEnumerateDirectories(string path)
        {
            try { return Directory.GetDirectories(path); }
            catch { return []; }
        }
    }
}
