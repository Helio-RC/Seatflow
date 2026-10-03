using System.Buffers.Binary;

namespace SeatFlow.Infrastructure.Tests.Exporters;

public class ImageSeatingExporterTests
{
    [Fact]
    public async Task ExportLayoutAsync_ShouldCreateBoundedPng()
    {
        var model = new LayoutSeatingExportModel
        {
            LayoutName = "预览布局",
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
            Format = ExportFormat.Png,
            HeaderTitle = "座位表  2026-10-03 00:36",
            HeaderSubtitle = "会场：A ｜ 名单：B ｜ 视角：学生视角",
            FooterNote = "By SeatFlow v2.1.0"
        };
        var exporter = new ImageSeatingExporter();
        var path = Path.GetTempFileName() + ".png";

        try
        {
            await exporter.ExportLayoutAsync(model, path, options, TestContext.Current.CancellationToken);

            var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            bytes.Length.Should().BeGreaterThan(100);
            bytes[..8].Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            int width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
            int height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
            width.Should().BeInRange(100, 2000);
            height.Should().BeInRange(100, 2000);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
