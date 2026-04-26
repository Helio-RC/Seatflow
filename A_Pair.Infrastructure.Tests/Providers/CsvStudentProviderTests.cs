using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Models;
using CsvHelper;

namespace A_Pair.Infrastructure.Tests.Providers;

public class CsvStudentProviderTests
{
    private string CreateTempCsv (string content)
    {
        var path = Path.GetTempFileName() + ".csv";
        File.WriteAllText(path , content , Encoding.UTF8);
        return path;
    }

    [Fact]
    public async Task LoadAsync_ValidCsv_ShouldReturnStudents ()
    {
        var csvContent = "Id,Name,Height,Gender,NeedsFrontRow,FrontRowPreferenceScore\n" +
                         "s1,Alice,165,Female,false,0\n" +
                         "s2,Bob,180,Male,true,10";
        var path = CreateTempCsv(csvContent);
        try
        {
            var provider = new CsvStudentProvider();
            var students = await provider.LoadAsync(path , CancellationToken.None);
            students.Should().HaveCount(2);
            students[0].Id.Should().Be("s1");
            students[0].Name.Should().Be("Alice");
            students[1].NeedsFrontRow.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ShouldReturnEmptyList ()
    {
        var path = CreateTempCsv("Id,Name\n");
        try
        {
            var provider = new CsvStudentProvider();
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
        var provider = new CsvStudentProvider();
        var students = await provider.LoadAsync("nonexistent.csv" , CancellationToken.None);
        students.Should().BeEmpty();
    }
}