using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Enums;
using A_Pair.Core.Models;
using OfficeOpenXml;

namespace A_Pair.Infrastructure.Tests.Writers;

public class XlsxStudentWriterTests
{
    [Fact]
    public async Task WriteAsync_ThenReadBack_ShouldMatch ()
    {
        var students = new List<Student>
        {
            new() { Id = "1", Name = "Alice", Height = 165, Gender = Gender.Female, NeedsFrontRow = true },
            new() { Id = "2", Name = "Bob", Height = 180, Gender = Gender.Male, NeedsFrontRow = false }
        };
        var path = Path.GetTempFileName() + ".xlsx";
        try
        {
            var writer = new XlsxStudentWriter();
            await writer.WriteAsync(path , students , CancellationToken.None);

            // 读取验证
            var provider = new XlsxStudentProvider();
            var loaded = await provider.LoadAsync(path , CancellationToken.None);
            loaded.Should().HaveCount(2);
            loaded.First().Name.Should().Be("Alice");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}