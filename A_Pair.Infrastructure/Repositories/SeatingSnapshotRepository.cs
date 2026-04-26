using System.Text.Json;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;

namespace A_Pair.Infrastructure.Repositories
{
    /// <summary>
    /// 座位快照仓储，将 <see cref="SeatingSnapshot"/> 以 JSON 格式持久化到本地文件系统。
    /// </summary>
    /// <remarks>
    /// 每个快照保存为独立的 JSON 文件，文件名格式为 <c><snapshotId>.json</c>。
    /// 支持按会场 ID 过滤快照列表，以及按创建时间降序排列。
    /// 损坏的文件会被自动跳过。
    /// </remarks>
    /// <param name="basePath">快照存储的基目录路径。</param>
    public class SeatingSnapshotRepository : ISeatingSnapshotRepository
    {
        private readonly string _basePath;

        /// <summary>
        /// 初始化快照仓储，确保存储目录存在。
        /// </summary>
        /// <param name="basePath">快照存储的基目录路径。</param>
        public SeatingSnapshotRepository (string basePath)
        {
            _basePath = basePath;
            Directory.CreateDirectory(_basePath);
        }

        /// <summary>
        /// 保存快照到 JSON 文件。
        /// </summary>
        /// <param name="snapshot">要保存的快照对象。</param>
        public async Task SaveAsync (SeatingSnapshot snapshot)
        {
            var path = Path.Combine(_basePath , snapshot.Id + ".json");
            var json = JsonSerializer.Serialize(snapshot , new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path , json);
        }

        /// <summary>
        /// 根据 ID 加载快照。
        /// </summary>
        /// <param name="id">快照 ID。</param>
        /// <returns>反序列化的快照对象；如果文件不存在则返回 <c>null</c>。</returns>
        public SeatingSnapshot? Load (string id)
        {
            var path = Path.Combine(_basePath , id + ".json");
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SeatingSnapshot>(json);
        }

        /// <summary>
        /// 获取指定会场的所有快照，按创建时间降序排列。
        /// </summary>
        /// <param name="venueId">会场 ID。</param>
        /// <returns>匹配的快照列表。</returns>
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
                    // 忽略损坏的文件
                }
            }
            return snapshots.OrderByDescending(s => s.CreatedAt).ToList();
        }

        /// <summary>
        /// 获取所有快照，按创建时间降序排列。
        /// </summary>
        /// <returns>所有快照的列表。</returns>
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

        /// <summary>
        /// 删除指定 ID 的快照文件。
        /// </summary>
        /// <param name="id">要删除的快照 ID。</param>
        public Task DeleteAsync (string id)
        {
            var path = Path.Combine(_basePath , id + ".json");
            if (File.Exists(path))
                File.Delete(path);
            return Task.CompletedTask;
        }
    }
}