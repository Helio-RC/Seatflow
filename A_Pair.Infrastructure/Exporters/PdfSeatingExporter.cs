using A_Pair.Core.Exporters;
using A_Pair.Core.Workspace;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace A_Pair.Infrastructure.Exporters
{
    public class PdfSeatingExporter : ISeatingPlanExporter
    {
        static PdfSeatingExporter()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public Task ExportAsync(SeatingPlan plan, string path, CancellationToken cancellationToken = default)
        {
            // 注：SeatingPlan 仅包含 Assignments，不包含座位几何信息，此处生成简化表格
            // 实际应用中应传入 ClassroomLayout 以绘制座位图
            QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    page.Header()
                        .Text("座位安排表")
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
                                header.Cell().Text("学生ID").Bold();
                            });

                            foreach (var kv in plan.Assignments.OrderBy(x => x.Key))
                            {
                                table.Cell().Text(kv.Key);
                                table.Cell().Text(kv.Value);
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