using System.Text.Json;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;

namespace A_Pair.Infrastructure.Providers;

public class JsonStudentDatasetRepository : IStudentDatasetRepository
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _rostersPath;

    public JsonStudentDatasetRepository(string rostersPath)
    {
        _rostersPath = rostersPath;
        Directory.CreateDirectory(_rostersPath);
    }

    public async Task SaveAsync(string id, string name, List<Student> students,
        string? originalFileName = null, CancellationToken ct = default)
    {
        var roster = new RosterFile
        {
            Version = "1.0",
            Description = name,
            Students = students,
            Metadata = new Dictionary<string, object>
            {
                ["importedAt"] = DateTime.UtcNow.ToString("O"),
                ["studentCount"] = students.Count
            }
        };
        if (originalFileName != null)
            roster.Metadata["originalFileName"] = originalFileName;

        var path = GetFilePath(id);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, roster, WriteOptions, ct);
    }

    public async Task<List<Student>?> LoadAsync(string id, CancellationToken ct = default)
    {
        var path = GetFilePath(id);
        if (!File.Exists(path)) return null;

        await using var stream = File.OpenRead(path);
        var roster = await JsonSerializer.DeserializeAsync<RosterFile>(stream, ReadOptions, ct);
        return roster?.Students;
    }

    public Task<IReadOnlyList<StudentDatasetInfo>> ListAsync(CancellationToken ct = default)
    {
        var results = new List<StudentDatasetInfo>();
        if (!Directory.Exists(_rostersPath))
            return Task.FromResult<IReadOnlyList<StudentDatasetInfo>>(results);

        foreach (var file in Directory.EnumerateFiles(_rostersPath, "*.roster.json"))
        {
            try
            {
                using var stream = File.OpenRead(file);
                var roster = JsonSerializer.Deserialize<RosterFile>(stream, ReadOptions);
                if (roster is null) continue;

                var info = new StudentDatasetInfo
                {
                    Id = Path.GetFileNameWithoutExtension(file),
                    Name = roster.Description ?? "未命名",
                    StudentCount = roster.Students.Count,
                    CreatedAt = ExtractDate(roster, file)
                };

                if (roster.Metadata.TryGetValue("originalFileName", out var origObj))
                    info.OriginalFileName = origObj?.ToString();

                results.Add(info);
            }
            catch
            {
                // 跳过损坏文件
            }
        }

        results.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
        return Task.FromResult<IReadOnlyList<StudentDatasetInfo>>(results);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var path = GetFilePath(id);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetFilePath(string id) => Path.Combine(_rostersPath, $"{id}.roster.json");

    private static DateTime ExtractDate(RosterFile roster, string filePath)
    {
        if (roster.Metadata.TryGetValue("importedAt", out var val) && val is JsonElement je)
        {
            var s = je.GetString();
            if (DateTime.TryParse(s, out var dt))
                return dt;
        }
        return File.GetCreationTimeUtc(filePath);
    }
}
