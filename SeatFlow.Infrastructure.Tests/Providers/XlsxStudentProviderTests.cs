using OfficeOpenXml;

namespace SeatFlow.Infrastructure.Tests.Providers;

public class XlsxStudentProviderTests
{
    private static string CreateTempXlsx (string[,] data)
    {
        var path = Path.GetTempFileName() + ".xlsx";
        ExcelPackage.License.SetNonCommercialPersonal("SeatFlow.Test");
        using (var package = new ExcelPackage(new FileInfo(path)))
        {
            var ws = package.Workbook.Worksheets.Add("Students");
            for (int r = 0; r <= data.GetUpperBound(0); r++)
            {
                for (int c = 0; c <= data.GetUpperBound(1); c++)
                {
                    ws.Cells[r + 1 , c + 1].Value = data[r , c];
                }
            }
            package.Save();
        }
        return path;
    }

    [Fact]
    public async Task LoadAsync_ValidXlsx_ShouldReturnStudents ()
    {
        var data = new string[,]
        {
            { "姓名", "身高", "性别", "需要前排" },        // row 1: header
            { "必填", "cm", "男/女", "是/否" },              // row 2: comment (skipped)
            { "Alice", "165", "女", "否" },                   // row 3: data
            { "Bob", "180", "男", "是" }                      // row 4: data
        };
        var path = CreateTempXlsx(data);
        try
        {
            var provider = new XlsxStudentProvider();
            var students = await provider.LoadAsync(path , CancellationToken.None);
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
    public async Task LoadAsync_EnglishHeaders_ShouldReturnStudents ()
    {
        var data = new string[,]
        {
            { "Name", "Height", "Gender", "NeedsFrontRow" }, // row 1: header
            { "Required", "cm", "Male/Female", "true/false" }, // row 2: comment (skipped)
            { "Alice", "165", "Female", "false" },            // row 3: data
            { "Bob", "180", "Male", "true" }                  // row 4: data
        };
        var path = CreateTempXlsx(data);
        try
        {
            var provider = new XlsxStudentProvider();
            var students = await provider.LoadAsync(path , CancellationToken.None);
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
    public async Task LoadAsync_EmptySheet_ShouldReturnEmptyList ()
    {
        var data = new string[,] { { "姓名" , "身高" } };
        var path = CreateTempXlsx(data);
        try
        {
            var provider = new XlsxStudentProvider();
            var students = await provider.LoadAsync(path , CancellationToken.None);
            students.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
