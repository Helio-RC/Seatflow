using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Models;

namespace A_Pair.Infrastructure.Tests.Providers;

public class JsonStudentProviderTests
{
    private string CreateTempJson (string json)
    {
        var path = Path.GetTempFileName() + ".json";
        File.WriteAllText(path , json , Encoding.UTF8);
        return path;
    }

    [Fact]
    public async Task LoadAsync_ValidJson_ShouldReturnStudents ()
    {
        var json = "[{\"Id\":\"1\",\"Name\":\"Alice\"},{\"Id\":\"2\",\"Name\":\"Bob\"}]";
        var path = CreateTempJson(json);
        try
        {
            var provider = new JsonStudentProvider();
            var students = await provider.LoadAsync(path , CancellationToken.None);
            students.Should().HaveCount(2);
            students[0].Id.Should().Be("1");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ShouldReturnEmptyList ()
    {
        var path = CreateTempJson("[]");
        try
        {
            var provider = new JsonStudentProvider();
            var students = await provider.LoadAsync(path , CancellationToken.None);
            students.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_FileNotFound_ShouldReturnEmptyList ()
    {
        var provider = new JsonStudentProvider();
        var students = await provider.LoadAsync("nonexistent.json" , CancellationToken.None);
        students.Should().BeEmpty();
    }
}