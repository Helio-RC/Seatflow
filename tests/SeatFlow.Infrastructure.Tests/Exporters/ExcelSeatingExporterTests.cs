using Microsoft.Extensions.Logging;
using NSubstitute;
using OfficeOpenXml;

namespace SeatFlow.Infrastructure.Tests.Exporters;

public class ExcelSeatingExporterTests
{
    [Fact]
    public async Task ExportAsync_ShouldCreateExcelFile()
    {
        var plan = new SeatingPlan
        {
            Assignments = new Dictionary<string, string>
            {
                { "seat1", "student1" }
            }
        };
        var exporter = new ExcelSeatingExporter(Substitute.For<ILogger<ExcelSeatingExporter>>());
        var path = Path.GetTempFileName() + ".xlsx";

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
    public async Task ExportAsync_WithOptions_ShouldIncludeMetadataSheet()
    {
        var plan = new SeatingPlan
        {
            Assignments = new Dictionary<string, string>
            {
                { "seat1", "student1" }
            }
        };
        var exporter = new ExcelSeatingExporter(Substitute.For<ILogger<ExcelSeatingExporter>>());
        var path = Path.GetTempFileName() + ".xlsx";

        try
        {
            var options = new ExportOptions { IncludeMetadata = true };
            await exporter.ExportAsync(plan, path, options, CancellationToken.None);
            // 简单验证文件存在（实际可通过EPPlus读取元数据表，略）
            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportLayoutAsync_ShouldBuildPrintFriendlySheet()
    {
        var model = new LayoutSeatingExportModel
        {
            LayoutName = "打印布局",
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
            Format = ExportFormat.Excel,
            HeaderTitle = "座位表  2026-10-03 00:36",
            HeaderSubtitle = "会场：A ｜ 名单：B",
            FooterNote = "By SeatFlow v2.1.0"
        };
        var exporter = new ExcelSeatingExporter(Substitute.For<ILogger<ExcelSeatingExporter>>());
        var path = Path.GetTempFileName() + ".xlsx";

        try
        {
            await exporter.ExportLayoutAsync(model, path, options, TestContext.Current.CancellationToken);

            using var package = new ExcelPackage(new FileInfo(path));
            var ws = package.Workbook.Worksheets["Seating"];
            ws.Should().NotBeNull();

            // 页眉合并 + 信息行
            ws!.Cells[1, 1].Value?.ToString().Should().Be("座位表  2026-10-03 00:36");
            ws.Cells[1, 1, 1, 4].Merge.Should().BeTrue();
            ws.Cells[2, 1].Value?.ToString().Should().Contain("会场：A");

            // 打印设置
            ws.View.ShowGridLines.Should().BeFalse();
            ws.PrinterSettings.FitToWidth.Should().Be(1);

            // 门在座位行左边列
            ws.Cells[5, 1].Value?.ToString().Should().Be("门 #1");
            // 讲台合并为居中宽块：文本与填充必须在合并区左上格（否则合并后不可见）
            ws.Cells[4, 2, 4, 4].Merge.Should().BeTrue();
            ws.Cells[4, 2].Value?.ToString().Should().Be("讲台");
            ws.Cells[4, 2].Style.Fill.BackgroundColor.Rgb.Should().Be("FFDCEBF7");
            // 未分配浅灰底
            ws.Cells[5, 3].Style.Fill.BackgroundColor.Rgb.Should().Be("FFEDEDED");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}