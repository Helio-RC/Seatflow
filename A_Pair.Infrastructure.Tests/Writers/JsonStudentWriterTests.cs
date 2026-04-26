using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Models;

namespace A_Pair.Infrastructure.Tests.Writers;

public class JsonStudentWriterTests
{
    [Fact]
    public async Task WriteAsync_ShouldCreateRosterFile ()
    {
        var students = new List<Student>
        {
            new() { Id = "1", Name = "Alice" },
            new() { Id = "2", Name = "Bob" }
        };
        var path = Path.GetTempFileName() + ".json";
        try
        {
            var writer = new JsonStudentWriter();
            await writer.WriteAsync(path , students , CancellationToken.None);

            var json = await File.ReadAllTextAsync(path);
            json.Should().Contain("alice"); // camelCase 命名策略
            var roster = JsonSerializer.Deserialize<RosterFile>(json , new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            roster.Should().NotBeNull();
            roster!.Students.Should().HaveCount(2);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}