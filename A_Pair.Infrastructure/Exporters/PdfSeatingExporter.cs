using A_Pair.Core.Exporters;
using A_Pair.Core.Models;
using A_Pair.Core.Workspace;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace A_Pair.Infrastructure.Exporters
{
    /// <summary>
    /// PDF 格式的座位安排导出器，使用 QuestPDF 库生成 A4 格式的 PDF 文档。
    /// </summary>
    /// <remarks>
    /// 生成包含标题、表格（座位 ID / 学生 ID）和页脚（生成时间）的 PDF 文档。
    /// 使用 QuestPDF 社区版许可证。当 <see cref="ExportOptions.Anonymize"/> 为 true 时，
    /// 标题和表格中的学生 ID 将被匿名化处理。
    /// </remarks>
    public class PdfSeatingExporter : ISeatingPlanExporter
    {
        /// <summary>
        /// 静态构造函数，设置 QuestPDF 为社区版许可证。
        /// </summary>
        static PdfSeatingExporter ()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        /// <inheritdoc />
        public Task ExportAsync (SeatingPlan plan , string path , CancellationToken cancellationToken = default)
        {
            return ExportAsync(plan , path , new ExportOptions { Format = ExportFormat.Pdf } , cancellationToken);
        }

        /// <inheritdoc />
        public Task ExportAsync (SeatingPlan plan , string path , ExportOptions options , CancellationToken cancellationToken = default)
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2 , Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    page.Header()
                        .Text(options.Anonymize ? "座位安排表 (匿名)" : "座位安排表")
                        .SemiBold().FontSize(20).AlignCenter();

                    page.Content()
                        .Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("座位ID").Bold();
                                header.Cell().Text(options.Anonymize ? "学生ID (匿名)" : "学生ID").Bold();
                            });

                            foreach (var kv in plan.Assignments.OrderBy(x => x.Key))
                            {
                                table.Cell().Text(kv.Key);
                                table.Cell().Text(options.Anonymize ? "***" : kv.Value);
                            }
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("生成时间: ");
                            x.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                        });
                });
            }).GeneratePdf(path);

            return Task.CompletedTask;
        }
    }
}