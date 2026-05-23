using System.Text.Json;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Infrastructure.Providers;

public class JsonStudentProvider : IStudentProvider
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<JsonStudentProvider> _logger;

    public JsonStudentProvider(ILogger<JsonStudentProvider>? logger = null)
    {
        _logger = logger ?? NullLogger<JsonStudentProvider>.Instance;
    }

    public async Task<List<Student>> LoadAsync (string source , CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(source) || !File.Exists(source)) return [];

        try
        {
            await using var stream = File.OpenRead(source);
            var roster = await JsonSerializer.DeserializeAsync<RosterFile>(stream , Options , cancellationToken);
            _logger.LogInformation("JSON 学生数据已加载：{Source}（{Count} 人）",
                source, roster?.Students.Count ?? 0);
            return roster?.Students ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON 学生数据解析失败：{Source}", source);
            return [];
        }
    }
}
