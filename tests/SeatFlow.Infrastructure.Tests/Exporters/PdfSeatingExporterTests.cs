namespace SeatFlow.Infrastructure.Tests.Exporters;

public class PdfSeatingExporterTests
{
    [Fact]
    public async Task ExportAsync_ShouldCreatePdfFile()
    {
        var plan = new SeatingPlan
        {
            Assignments = new Dictionary<string, string>
            {
                { "seat1", "student1" }
            }
        };
        var exporter = new PdfSeatingExporter();
        var path = Path.GetTempFileName() + ".pdf";

        try
        {
            await exporter.ExportAsync(plan, path, CancellationToken.None);
            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportLayoutAsync_ShouldCreatePdfWithPosterStyle()
    {
        var model = new LayoutSeatingExportModel
        {
            LayoutName = "海报布局",
            LayoutType = LayoutType.Grid,
            Rows =
            [
                new ExportRow
                {
                    Cells =
                    [
                        new ExportCell { Text = "" },
                        new ExportCell { IsPodium = true, Text = "讲台" },
                        new ExportCell { Text = "" }
                    ]
                },
                new ExportRow
                {
                    Cells =
                    [
                        new ExportCell { IsSeat = true, Text = "张三" },
                        new ExportCell { IsSeat = true, IsUnassigned = true, Text = "未分配" },
                        new ExportCell { IsSeat = true, Text = "李四" }
                    ],
                    LeftMarginText = "门 #1"
                }
            ]
        };
        var options = new ExportOptions
        {
            Format = ExportFormat.Pdf,
            HeaderTitle = "座位表  2026-10-03 00:36",
            HeaderSubtitle = "会场：A ｜ 名单：B",
            FooterNote = "By SeatFlow v2.1.0"
        };
        var exporter = new PdfSeatingExporter();
        var path = Path.GetTempFileName() + ".pdf";

        try
        {
            await exporter.ExportLayoutAsync(model, path, options, TestContext.Current.CancellationToken);

            File.Exists(path).Should().BeTrue();
            var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            bytes.Length.Should().BeGreaterThan(500);
            System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportLayoutAsync_EmptyModel_ShouldStillCreatePdf()
    {
        var model = new LayoutSeatingExportModel
        {
            LayoutName = "空布局",
            LayoutType = LayoutType.Grid,
            Rows = []
        };
        var options = new ExportOptions { Format = ExportFormat.Pdf, HeaderTitle = "座位表" };
        var exporter = new PdfSeatingExporter();
        var path = Path.GetTempFileName() + ".pdf";

        try
        {
            await exporter.ExportLayoutAsync(model, path, options, TestContext.Current.CancellationToken);

            File.Exists(path).Should().BeTrue();
            var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}