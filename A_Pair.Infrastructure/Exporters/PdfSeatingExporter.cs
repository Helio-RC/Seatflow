using A_Pair.Core.Exporters;
using A_Pair.Core.Models;
using A_Pair.Core.Workspace;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace A_Pair.Infrastructure.Exporters
{
    public class PdfSeatingExporter : ISeatingPlanExporter
    {
        public ExportFormat Format => ExportFormat.Pdf;
        static PdfSeatingExporter ()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public Task ExportAsync (SeatingPlan plan , string path , CancellationToken cancellationToken = default)
        {
            return ExportAsync(plan , path , new ExportOptions { Format = ExportFormat.Pdf } , cancellationToken);
        }

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

        public Task ExportLayoutAsync (LayoutSeatingExportModel model , string path , ExportOptions options , CancellationToken cancellationToken = default)
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1 , Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header()
                        .Text(model.LayoutName)
                        .SemiBold().FontSize(18).AlignCenter();

                    page.Content()
                        .Table(table =>
                        {
                            int maxCols = model.Rows.Count > 0 ? model.Rows.Max(r => r.Cells.Count) : 1;
                            table.ColumnsDefinition(columns =>
                            {
                                for (int i = 0; i < maxCols; i++)
                                    columns.ConstantColumn(36);
                            });

                            foreach (var row in model.Rows)
                            {
                                foreach (var cell in row.Cells)
                                {
                                    table.Cell()
                                        .Border(1).BorderColor(Colors.Grey.Lighten2)
                                        .Background(cell.IsPodium ? Colors.Blue.Lighten4 :
                                                     cell.IsAisle ? Colors.Grey.Lighten3 :
                                                     cell.IsSeat ? Colors.Green.Lighten5 :
                                                     Colors.White)
                                        .Padding(2)
                                        .AlignCenter()
                                        .Text(cell.Text);
                                }
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
