using System.Text.Json;
using System.Text.Json.Nodes;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Core.Storage;
using SeatFlow.Infrastructure.Migration;
using SeatFlow.Infrastructure.Serialization;
using SeatFlow.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Repositories
{
    public class SeatingSnapshotRepository : ISeatingSnapshotRepository
    {
        private readonly ILocalDataStore _store;
        private readonly string _baseDir;
        private readonly FileMigrationService _migration;
        private readonly Dictionary<string, string> _index = [];
        private bool _indexBuilt;
        private readonly Lock _indexLock = new();
        private readonly ILogger<SeatingSnapshotRepository> _logger;

        /// <summary>
        /// 初始化快照仓储（存储抽象版本）。
        /// </summary>
        /// <param name="store">数据存储实现。</param>
        /// <param name="baseDir">快照根目录（相对存储根，如 <c>Assignments</c>）。</param>
        /// <param name="migration">文件迁移服务。</param>
        /// <param name="logger">日志记录器。</param>
        public SeatingSnapshotRepository(
            ILocalDataStore store,
            string baseDir,
            FileMigrationService migration,
            ILogger<SeatingSnapshotRepository>? logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _baseDir = NormalizeDir(baseDir);
            _migration = migration ?? throw new ArgumentNullException(nameof(migration));
            _logger = logger ?? NullLogger<SeatingSnapshotRepository>.Instance;
        }

        /// <summary>兼容构造：直接指定文件系统目录。</summary>
        public SeatingSnapshotRepository(
            string basePath,
            FileMigrationService migration,
            ILogger<SeatingSnapshotRepository>? logger = null)
            : this(new FileSystemDataStore(basePath), "", migration, logger)
        {
        }

        private static string GetRelativePath(string baseDir, string venueId, DateTime date, string id)
            => Join(baseDir, venueId, date.ToString("yyyyMMdd"), id + ".json");

        private static string Join(params string[] parts)
        {
            var items = parts.Where(p => !string.IsNullOrEmpty(p)).ToArray();
            return string.Join('/', items);
        }

        private static string NormalizeDir(string dir)
            => (dir ?? "").Trim().Trim('/', '\\');

        private async Task EnsureIndexAsync(CancellationToken ct = default)
        {
            if (_indexBuilt) return;
            lock (_indexLock)
            {
                if (_indexBuilt) return;
            }

            // 异步扫描（锁外执行，避免阻塞；完成后原子置位）
            var index = new Dictionary<string, string>();
            foreach (var venueRel in await _store.ListDirectoriesAsync(_baseDir, ct))
            {
                var venueFull = Join(_baseDir, venueRel);
                foreach (var dateRel in await _store.ListDirectoriesAsync(venueFull, ct))
                {
                    var dateFull = Join(venueFull, dateRel);
                    foreach (var file in await _store.ListAsync(dateFull, "*.json", ct))
                    {
                        var id = Path.GetFileNameWithoutExtension(file[(file.LastIndexOf('/') + 1)..]);
                        if (id is { Length: > 0 } && !index.ContainsKey(id))
                            index[id] = file;
                    }
                }
            }

            lock (_indexLock)
            {
                if (!_indexBuilt)
                {
                    _index.Clear();
                    foreach (var (k, v) in index)
                        _index[k] = v;
                    _indexBuilt = true;
                }
            }
        }

        public async Task SaveAsync(SeatingSnapshot snapshot, CancellationToken ct = default)
        {
            await EnsureIndexAsync(ct);
            var relPath = GetRelativePath(_baseDir, snapshot.LayoutId, snapshot.CreatedAt, snapshot.Id);
            snapshot.Version = FileVersionInfo.GetCurrentVersion("snapshot");
            var json = JsonSerializer.Serialize(snapshot, JsonOptions.WriteIndented);
            await _store.WriteTextAsync(relPath, json, ct);
            lock (_indexLock)
            {
                _index[snapshot.Id] = relPath;
            }
            _logger.LogInformation("快照已保存：{SnapshotId} → {Path}", snapshot.Id, relPath);
        }

        public async Task SaveVenueInfoAsync(string venueId, VenueSnapshotInfo info, CancellationToken ct = default)
        {
            var relPath = Join(_baseDir, venueId, "_venue.json");
            info.Version = FileVersionInfo.GetCurrentVersion("venueInfo");
            var json = JsonSerializer.Serialize(info, JsonOptions.WriteIndented);
            await _store.WriteTextAsync(relPath, json, ct);
            _logger.LogInformation("会场快照信息已保存: {VenueId}", venueId);
        }

        public async Task<SeatingSnapshot?> LoadAsync(string id, CancellationToken ct = default)
        {
            await EnsureIndexAsync(ct);
            string? relPath = null;
            lock (_indexLock)
            {
                relPath = _index.GetValueOrDefault(id);
            }
            if (relPath is null) return null;

            var json = await _store.ReadTextAsync(relPath, ct);
            if (json is null) return null;
            var snapshot = DeserializeWithMigration(json, "snapshot");
            _logger.LogDebug("快照已加载: {SnapshotId}", id);
            return snapshot;
        }

        public async Task<IReadOnlyList<SeatingSnapshot>> ListByVenueAsync(string venueId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var snapshots = await LoadFromDirAsync(Join(_baseDir, venueId), ct);
            _logger.LogDebug("列出会场 {VenueId} 的 {Count} 个快照", venueId, snapshots.Count);
            return [.. snapshots.OrderByDescending(s => s.CreatedAt)];
        }

        public async Task<IReadOnlyList<SeatingSnapshot>> ListAllAsync()
        {
            var snapshots = new List<SeatingSnapshot>();
            foreach (var venueRel in await _store.ListDirectoriesAsync(_baseDir))
            {
                snapshots.AddRange(await LoadFromDirAsync(Join(_baseDir, venueRel)));
            }
            _logger.LogDebug("列出所有 {Count} 个快照", snapshots.Count);
            return [.. snapshots.OrderByDescending(s => s.CreatedAt)];
        }

        public async Task DeleteAsync(string id, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            await EnsureIndexAsync(ct);
            string? relPath;
            lock (_indexLock)
            {
                relPath = _index.GetValueOrDefault(id);
            }
            if (relPath is null)
            {
                _logger.LogDebug("删除快照未找到：{SnapshotId}", id);
                return;
            }

            await _store.DeleteAsync(relPath, ct);
            lock (_indexLock)
            {
                _index.Remove(id);
            }
            _logger.LogInformation("快照已删除：{SnapshotId}", id);
        }

        private SeatingSnapshot? DeserializeWithMigration(string json, string fileType)
        {
            var node = JsonNode.Parse(json);
            if (node is not null)
            {
                var fileVersion = node["version"]?.GetValue<string>() ?? "1.0";
                node = _migration.Migrate(fileType, node, fileVersion, FileVersionInfo.GetCurrentVersion(fileType));
                json = node.ToJsonString();
            }
            return JsonSerializer.Deserialize<SeatingSnapshot>(json);
        }

        private async Task<List<SeatingSnapshot>> LoadFromDirAsync(string dir, CancellationToken ct = default)
        {
            var snapshots = new List<SeatingSnapshot>();
            foreach (var dateRel in await _store.ListDirectoriesAsync(dir, ct))
            {
                var dateFull = Join(dir, dateRel);
                foreach (var file in await _store.ListAsync(dateFull, "*.json", ct))
                {
                    try
                    {
                        var json = await _store.ReadTextAsync(file, ct);
                        if (json is null) continue;
                        var snapshot = DeserializeWithMigration(json, "snapshot");
                        if (snapshot is not null) snapshots.Add(snapshot);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "跳过损坏的快照文件：{File}", file);
                    }
                }
            }
            return snapshots;
        }
    }
}
