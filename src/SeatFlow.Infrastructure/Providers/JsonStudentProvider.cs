using System.Text.Json;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Providers;

public class JsonStudentProvider : IStudentProvider
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILocalDataStore? _store;
    private readonly ILogger<JsonStudentProvider> _logger;

    /// <param name="store">存储抽象（可选；非空时支持存储相对路径源）。</param>
    /// <param name="logger">日志记录器。</param>
    public JsonStudentProvider (ILocalDataStore? store = null , ILogger<JsonStudentProvider>? logger = null)
    {
        _store = store;
        _logger = logger ?? NullLogger<JsonStudentProvider>.Instance;
    }

    public Task<List<Student>> LoadAsync (string source , CancellationToken cancellationToken = default)
    {
        return LoadAsync(source , 0 , 0 , cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<Student>> LoadAsync (string source , int maxRows , int maxCols , CancellationToken ct = default)
    {
        var bytes = await StudentSourceResolver.ReadBytesAsync(source , _store , ct);
        if (bytes is null) return [];

        try
        {
            await using var stream = new MemoryStream(bytes);
            var roster = await JsonSerializer.DeserializeAsync<RosterFile>(stream , Options , ct);
            _logger.LogInformation("JSON 学生数据已加载：{Source}（{Count} 人）" ,
                source , roster?.Students.Count ?? 0);
            var students = roster?.Students ?? [];

            if (maxRows > 0 && students.Count > maxRows)
                students = students.Take(maxRows).ToList();

            return students;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex , "JSON 学生数据解析失败：{Source}" , source);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<(int Rows , int Cols)> GetDimensionsAsync (string source , CancellationToken ct = default)
    {
        var bytes = await StudentSourceResolver.ReadBytesAsync(source , _store , ct);
        if (bytes is null)
            return (0 , 0);

        try
        {
            using var stream = new MemoryStream(bytes);
            var roster = JsonSerializer.Deserialize<RosterFile>(stream , Options);
            int count = roster?.Students.Count ?? 0;
            return (count , 1);
        }
        catch (Exception)
        {
            return (0 , 0);
        }
    }
}
