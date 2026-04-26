using OfficeOpenXml;

namespace A_Pair.Infrastructure.Tests.Providers;

public class XlsxStudentProviderTests
{
    private string CreateTempXlsx (string[,] data)
    {
        var path = Path.GetTempFileName() + ".xlsx";
        ExcelPackage.License.SetNonCommercialPersonal("A_Pair.Test");
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
            { "Name", "Id" },
            { "Alice", "1" },
            { "Bob", "2" }
        };
        var path = CreateTempXlsx(data);
        try
        {
            var provider = new XlsxStudentProvider();
            var students = await provider.LoadAsync(path , CancellationToken.None);
            students.Should().HaveCount(2);
            students[0].Name.Should().Be("Alice");
            students[1].Id.Should().Be("2");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_EmptySheet_ShouldReturnEmptyList ()
    {
        var path = CreateTempXlsx(new string[,] { { "Name" , "Id" } });
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