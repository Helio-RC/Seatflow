namespace A_Pair.Infrastructure.Tests.Exporters;

public class CsvSeatingExporterTests
{
    [Fact]
    public async Task ExportAsync_ShouldCreateCsvFile ()
    {
        var plan = new SeatingPlan
        {
            Assignments = new Dictionary<string , string>
            {
                { "seat1", "student1" },
                { "seat2", "student2" }
            }
        };
        var exporter = new CsvSeatingExporter();
        var path = Path.GetTempFileName() + ".csv";

        try
        {
            await exporter.ExportAsync(plan , path , CancellationToken.None);
            var content = await File.ReadAllTextAsync(path , TestContext.Current.CancellationToken);
            content.Should().Contain("seat1,student1");
            content.Should().Contain("seat2,student2");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportAsync_WithAnonymize_ShouldMaskStudentId ()
    {
        var plan = new SeatingPlan
        {
            Assignments = new Dictionary<string , string>
            {
                { "seat1", "student1" }
            }
        };
        var exporter = new CsvSeatingExporter();
        var path = Path.GetTempFileName() + ".csv";

        try
        {
            var options = new ExportOptions { Format = ExportFormat.Csv , Anonymize = true };
            await exporter.ExportAsync(plan , path , options , CancellationToken.None);
            var content = await File.ReadAllTextAsync(path , TestContext.Current.CancellationToken);
            content.Should().Contain("***");
            content.Should().NotContain("student1");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportLayoutAsync_TeacherView_ShouldReverseRows ()
    {
        var model = new LayoutSeatingExportModel
        {
            LayoutName = "教师视角测试" ,
            LayoutType = LayoutType.Grid ,
            Rows =
            [
                new ExportRow { Cells = [new ExportCell { IsPodium = true , Text = "讲台" }] } ,
                new ExportRow { Cells = [new ExportCell { IsSeat = true , Text = "第一排" }] } ,
                new ExportRow { Cells = [new ExportCell { IsSeat = true , Text = "第二排" }] }
            ]
        };
        // 模拟 ApplicationFacade 中 TeacherView 时的反转
        model.Rows.Reverse();

        var exporter = new CsvSeatingExporter();
        var path = Path.GetTempFileName() + ".csv";

        try
        {
            var options = new ExportOptions { Format = ExportFormat.Csv , Perspective = LayoutPerspective.TeacherView };
            await exporter.ExportLayoutAsync(model , path , options , CancellationToken.None);
            var content = await File.ReadAllTextAsync(path , TestContext.Current.CancellationToken);

            // 反转后："第二排" 应在 "第一排" 之前出现（第二排在文件更上方）
            var secondRowIndex = content.IndexOf("第二排" , StringComparison.Ordinal);
            var firstRowIndex = content.IndexOf("第一排" , StringComparison.Ordinal);
            secondRowIndex.Should().BeLessThan(firstRowIndex);
            // 讲台应在最后
            var podiumIndex = content.IndexOf("讲台" , StringComparison.Ordinal);
            podiumIndex.Should().BeGreaterThan(firstRowIndex);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}