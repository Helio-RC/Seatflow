using System.Text;

namespace A_Pair.Infrastructure.Tests.Providers;

public class CsvStudentProviderTests
{
    private static string CreateTempCsv(string content)
    {
        var path = Path.GetTempFileName() + ".csv";
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    [Fact]
    public async Task LoadAsync_ValidCsv_ShouldReturnStudents()
    {
        var csvContent =
            "Name,Height,Gender,NeedsFrontRow\n" +
            "必填,cm,男/女,是/否\n" +
            "Alice,165,Female,false\n" +
            "Bob,180,Male,true";
        var path = CreateTempCsv(csvContent);
        try
        {
            var provider = new CsvStudentProvider();
            var students = await provider.LoadAsync(path, CancellationToken.None);
            students.Should().HaveCount(2);
            students[0].Name.Should().Be("Alice");
            students[1].NeedsFrontRow.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_ChineseHeaders_ShouldReturnStudents()
    {
        var csvContent =
            "姓名,身高,性别,需要前排\n" +
            "必填,厘米,男/女/其他,是/否\n" +
            "Alice,165,女,否\n" +
            "Bob,180,男,是";
        var path = CreateTempCsv(csvContent);
        try
        {
            var provider = new CsvStudentProvider();
            var students = await provider.LoadAsync(path, CancellationToken.None);
            students.Should().HaveCount(2);
            students[0].Name.Should().Be("Alice");
            students[1].NeedsFrontRow.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ShouldReturnEmptyList()
    {
        var path = CreateTempCsv("Name,Height\n备注,cm\n");
        try
        {
            var provider = new CsvStudentProvider();
            var students = await provider.LoadAsync(path, CancellationToken.None);
            students.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_FileNotFound_ShouldReturnEmptyList()
    {
        var provider = new CsvStudentProvider();
        var students = await provider.LoadAsync("nonexistent.csv", CancellationToken.None);
        students.Should().BeEmpty();
    }
}
