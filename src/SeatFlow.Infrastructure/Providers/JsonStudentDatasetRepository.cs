using System.Text.Json;
using System.Text.Json.Nodes;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Core.Storage;
using SeatFlow.Infrastructure.Migration;
using SeatFlow.Infrastructure.Storage;
using SeatFlow.Infrastructure.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Providers;

public class JsonStudentDatasetRepository : IStudentDatasetRepository
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true ,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILocalDataStore _store;
    private readonly string _rostersDir;
    private readonly FileMigrationService _migration;
    private readonly ILogger<JsonStudentDatasetRepository> _logger;

    /// <summary>
    /// 初始化数据集仓储（存储抽象版本）。
    /// </summary>
    /// <param name="store">数据存储实现。</param>
    /// <param name="rostersDir">数据集目录（相对存储根，如 <c>Rosters</c>）。</param>
    /// <param name="migration">文件迁移服务。</param>
    /// <param name="logger">日志记录器。</param>
    public JsonStudentDatasetRepository (
        ILocalDataStore store ,
        string rostersDir ,
        FileMigrationService migration ,
        ILogger<JsonStudentDatasetRepository>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _rostersDir = NormalizeDir(rostersDir);
        _migration = migration ?? throw new ArgumentNullException(nameof(migration));
        _logger = logger ?? NullLogger<JsonStudentDatasetRepository>.Instance;
        _ = store.EnsureDirAsync(_rostersDir);
    }

    /// <summary>兼容构造：直接指定文件系统目录。</summary>
    public JsonStudentDatasetRepository (
        string rostersPath ,
        FileMigrationService migration ,
        ILogger<JsonStudentDatasetRepository>? logger = null)
        : this(new FileSystemDataStore(rostersPath) , "" , migration , logger)
    {
    }

    public async Task SaveAsync (string id , string name , List<Student> students ,
        string? originalFileName = null , CancellationToken ct = default)
    {
        var roster = new RosterFile
        {
            Version = FileVersionInfo.GetCurrentVersion("roster") ,
            Description = name ,
            Students = students ,
            Metadata = new Dictionary<string , object>
            {
                ["importedAt"] = DateTime.Now.ToString("O") ,
                ["studentCount"] = students.Count
            }
        };
        if (originalFileName != null)
            roster.Metadata["originalFileName"] = originalFileName;

        // 按 Id 排序后序列化学生列表，计算 StudentsHash
        roster.Students = [.. roster.Students.OrderBy(s => s.Id)];
        var studentsJson = JsonSerializer.Serialize(roster.Students , WriteOptions);
        roster.StudentsHash = ContentHashHelper.ComputeSha256(studentsJson);

        var path = GetFilePath(id);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(roster , WriteOptions);
        await _store.WriteBytesAsync(path , bytes , ct);
        _logger.LogInformation("学生数据集已保存：{Id}（{Count} 人）→ {Path}" , id , students.Count , path);
    }

    public async Task<List<Student>?> LoadAsync (string id , CancellationToken ct = default)
    {
        var path = GetFilePath(id);
        var json = await _store.ReadTextAsync(path , ct);
        if (json is null) return null;

        var roster = DeserializeRoster(json);
        _logger.LogInformation("学生数据集已加载: {DatasetId}" , id);
        return roster?.Students;
    }

    public async Task<IReadOnlyList<StudentDatasetInfo>> ListAsync (CancellationToken ct = default)
    {
        var results = new List<StudentDatasetInfo>();
        var files = await _store.ListAsync(_rostersDir , "*.roster.json" , ct);
        foreach (var file in files)
        {
            try
            {
                var json = await _store.ReadTextAsync(file , ct);
                if (json is null) continue;
                var roster = DeserializeRoster(json);
                if (roster is null) continue;

                var info = new StudentDatasetInfo
                {
                    Id = GetFileNameWithoutAnyExt(file) ,
                    Name = roster.Description ?? "未命名" ,
                    StudentCount = roster.Students.Count ,
                    CreatedAt = await ExtractDateAsync(roster , file , ct)
                };

                if (roster.Metadata.TryGetValue("originalFileName" , out var origObj))
                    info.OriginalFileName = origObj?.ToString();

                results.Add(info);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex , "跳过损坏的数据集文件：{File}" , file);
            }
        }

        results.Sort((a , b) => b.CreatedAt.CompareTo(a.CreatedAt));
        _logger.LogDebug("列出 {Count} 个学生数据集" , results.Count);
        return results;
    }

    public async Task DeleteAsync (string id , CancellationToken ct = default)
    {
        var path = GetFilePath(id);
        if (await _store.ExistsAsync(path , ct))
            await _store.DeleteAsync(path , ct);
        _logger.LogInformation("学生数据集已删除: {DatasetId}" , id);
    }

    public async Task RenameAsync (string id , string newName , CancellationToken ct = default)
    {
        var path = GetFilePath(id);
        var json = await _store.ReadTextAsync(path , ct);
        if (json is null)
            throw new FileNotFoundException($"数据集文件不存在：{path}");

        var roster = DeserializeRoster(json) ?? throw new InvalidOperationException($"数据集文件损坏：{path}");
        roster.Description = newName;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(roster , WriteOptions);
        await _store.WriteBytesAsync(path , bytes , ct);
        _logger.LogInformation("数据集已重命名：{Id} → {Name}" , id , newName);
    }

    private RosterFile? DeserializeRoster (string json)
    {
        var node = JsonNode.Parse(json);
        if (node is not null)
        {
            var fileVersion = node["version"]?.GetValue<string>() ?? "1.0";
            node = _migration.Migrate("roster" , node , fileVersion , FileVersionInfo.GetCurrentVersion("roster"));
            json = node.ToJsonString();
        }
        return JsonSerializer.Deserialize<RosterFile>(json , ReadOptions);
    }

    private static string GetFileNameWithoutAnyExt (string relativePath)
    {
        var name = relativePath[(relativePath.LastIndexOf('/') + 1)..];
        return Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(name));
    }

    private string GetFilePath (string id)
        => (_rostersDir.Length == 0 ? "" : _rostersDir + "/") + $"{id}.roster.json";

    private async Task<DateTime> ExtractDateAsync (RosterFile roster , string filePath , CancellationToken ct)
    {
        if (roster.Metadata.TryGetValue("importedAt" , out var val) && val is JsonElement je)
        {
            var s = je.GetString();
            if (DateTime.TryParse(s , out var dt))
                return dt;
        }
        return await _store.GetCreationTimeUtcAsync(filePath , ct);
    }

    private static string NormalizeDir (string dir)
        => (dir ?? "").Trim().Trim('/', '\\');
}
