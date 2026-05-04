using System.Text.Json;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;

namespace A_Pair.Infrastructure.Providers;

public class JsonStudentProvider : IStudentProvider
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<Student>> LoadAsync (string source , CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(source) || !File.Exists(source)) return [];

        try
        {
            await using var stream = File.OpenRead(source);
            var roster = await JsonSerializer.DeserializeAsync<RosterFile>(stream , Options , cancellationToken);
            return roster?.Students ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
