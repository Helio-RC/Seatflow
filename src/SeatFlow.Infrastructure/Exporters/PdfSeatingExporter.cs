#if !BROWSER
using SeatFlow.Core.Exporters;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SeatFlow.Infrastructure.Exporters;

public class PdfSeatingExporter(ILogger<PdfSeatingExporter>? logger = null) : ISeatingPlanExporter
{
    private const float PageMargin = 10f;

    // 预览海报风尺寸（mm）
    private const float SeatColWidth = 16f;
    private const float SeatRowHeight = 9f;
    private const float AisleColWidth = 5f;
    private const float AisleRowHeight = 5f;
    private const float MarginColWidth = 9f;
    private const float PodiumRowHeight = 12f;

    private readonly ILogger<PdfSeatingExporter> _logger = logger ?? NullLogger<PdfSeatingExporter>.Instance;

    public ExportFormat Format => ExportFormat.Pdf;

    static PdfSeatingExporter()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // QuestPDF 2026.9.0+：缺失字形检查始终执行，且系统字体不再默认参与回退。
        // 注册嵌入的 Noto Sans SC 作为 CJK 回退字体，保证中文座位表在任意环境可导出；
        // 系统字体作为额外回退（桌面环境字体更完整）。
        using var fontStream = typeof(PdfSeatingExporter).Assembly
            .GetManifestResourceStream("SeatFlow.Infrastructure.Assets.NotoSansSC-Regular.otf");
        if (fontStream is not null)
            QuestPDF.Drawing.FontManager.RegisterFontFromStream(fontStream);
        QuestPDF.Settings.UseSystemFonts = true;
    }

    public Task ExportAsync(SeatingPlan plan, string path, CancellationToken cancellationToken = default)
    {
        return ExportAsync(plan, path, new ExportOptions { Format = ExportFormat.Pdf }, cancellationToken);
    }

    public async Task ExportAsync(SeatingPlan plan, string path, ExportOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("PDF 座位表导出开始：{Path}（{Count} 条记录）", path, plan.Assignments.Count);

        await Task.Run(() =>
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontFamily("Lato", "Noto Sans SC").FontSize(12));

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
            _logger.LogInformation("PDF 座位表导出完成: {Path}", path);
        }, cancellationToken);
    }

    public async Task ExportLayoutAsync(LayoutSeatingExportModel model, string path, ExportOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("PDF 座位布局导出开始：{Path}（{RowCount} 行）", path, model.Rows.Count);

        var texts = options.Texts;
        int dataCols = model.Rows.Count > 0 ? Math.Max(1, model.Rows.Max(r => r.Cells.Count)) : 1;

        var colSeatCount = new int[dataCols];
        var colAisleCount = new int[dataCols];
        foreach (var row in model.Rows)
            for (int i = 0; i < row.Cells.Count && i < dataCols; i++)
            {
                if (row.Cells[i].IsSeat) colSeatCount[i]++;
                else if (row.Cells[i].IsAisle) colAisleCount[i]++;
            }

        var colWidths = new float[dataCols];
        for (int i = 0; i < dataCols; i++)
            colWidths[i] = colAisleCount[i] > 0 && colSeatCount[i] == 0 ? AisleColWidth : SeatColWidth;

        var rowHeights = new float[model.Rows.Count];
        for (int i = 0; i < model.Rows.Count; i++)
        {
            var row = model.Rows[i];
            bool isAisleRow = row.Cells.Count > 0 && row.Cells.All(c => c.IsAisle);
            rowHeights[i] = isAisleRow ? AisleRowHeight
                : row.Cells.Any(c => c.IsPodium) ? PodiumRowHeight
                : SeatRowHeight;
        }

        float leftMargin = model.HasLeftMargin ? MarginColWidth : 0f;
        float rightMargin = model.HasRightMargin ? MarginColWidth : 0f;
        float tableWidth = leftMargin + colWidths.Sum() + rightMargin;
        float tableHeight = rowHeights.Sum();

        var subtitle = options.HeaderSubtitle ?? model.LayoutName;
        float headerHeight = string.IsNullOrWhiteSpace(subtitle) ? 12f : 19f;
        float footerHeight = string.IsNullOrWhiteSpace(options.FooterNote) ? 2f : 8f;

        // 内容自适应页面：宽度必须容纳全部固定列，不设上限（超宽布局不再触发布局异常）；
        // 最小宽度需容纳页眉文案，避免长标题/副标题在窄页上换行溢出为多页。
        float pageWidth = Math.Max(tableWidth + (PageMargin * 2), 120f);
        float pageHeight = Math.Max(headerHeight + tableHeight + footerHeight + (PageMargin * 2) + 6f, 60f);

        await Task.Run(() =>
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(pageWidth, pageHeight, Unit.Millimetre);
                    page.Margin(PageMargin, Unit.Millimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontFamily("Lato", "Noto Sans SC").FontSize(7).FontColor("#2A354A"));

                    page.Content().Column(col =>
                    {
                        col.Item().AlignCenter()
                            .Text(options.HeaderTitle ?? texts.SeatingChart)
                            .FontSize(14).SemiBold().FontColor("#1F2A44");

                        if (!string.IsNullOrWhiteSpace(subtitle))
                            col.Item().PaddingTop(2).AlignCenter()
                                .Text(subtitle).FontSize(7).FontColor("#6B7280");

                        col.Item().PaddingTop(5).PaddingBottom(4)
                            .BorderBottom(0.5f).BorderColor("#E3E6EB");

                        col.Item().AlignCenter().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                if (model.HasLeftMargin)
                                    columns.ConstantColumn(leftMargin, Unit.Millimetre);
                                for (int i = 0; i < colWidths.Length; i++)
                                    columns.ConstantColumn(colWidths[i], Unit.Millimetre);
                                if (model.HasRightMargin)
                                    columns.ConstantColumn(rightMargin, Unit.Millimetre);
                            });

                            for (int rowIndex = 0; rowIndex < model.Rows.Count; rowIndex++)
                            {
                                if ((rowIndex + 1) % 30 == 0)
                                    cancellationToken.ThrowIfCancellationRequested();

                                var row = model.Rows[rowIndex];
                                float rowH = rowHeights[rowIndex];

                                // 左侧门标注
                                if (model.HasLeftMargin)
                                {
                                    if (!string.IsNullOrEmpty(row.LeftMarginText))
                                        table.Cell().Padding(1).Height(rowH, Unit.Millimetre)
                                            .Background(Colors.White).Border(0.8f).BorderColor("#90A8C0")
                                            .CornerRadius(1.5f).AlignMiddle().AlignCenter()
                                            .Text(row.LeftMarginText).FontSize(7).FontColor("#4A6B8A");
                                    else
                                        table.Cell().Height(rowH, Unit.Millimetre);
                                }

                                int podiumIndex = row.Cells.FindIndex(c => c.IsPodium);
                                int podiumSpan = 0;
                                int podiumStart = -1;
                                if (podiumIndex >= 0)
                                {
                                    podiumSpan = Math.Min(3, row.Cells.Count);
                                    podiumStart = Math.Clamp(podiumIndex - (podiumSpan - 1) / 2, 0,
                                        Math.Max(0, row.Cells.Count - podiumSpan));
                                }

                                for (int c = 0; c < row.Cells.Count; c++)
                                {
                                    if (c == podiumStart)
                                    {
                                        table.Cell().ColumnSpan((uint)podiumSpan).Padding(1)
                                            .Height(rowH, Unit.Millimetre)
                                            .Background("#E3F2FD").Border(0.8f).BorderColor("#90CAF9")
                                            .CornerRadius(2).AlignMiddle().AlignCenter()
                                            .Text(row.Cells[podiumIndex].Text).FontSize(8).SemiBold().FontColor("#1565C0");
                                        c += podiumSpan - 1;
                                        continue;
                                    }

                                    var cell = row.Cells[c];
                                    if (cell.IsSeat)
                                    {
                                        var bg = cell.IsUnassigned ? "#F2F2F2" : "#F4F7FE";
                                        var bd = cell.IsUnassigned ? "#CCCCCC" : "#AAB4E0";
                                        var fg = cell.IsUnassigned ? "#8A8A8A" : "#2A354A";
                                        table.Cell().Padding(1).Height(rowH, Unit.Millimetre)
                                            .Background(bg).Border(0.6f).BorderColor(bd)
                                            .CornerRadius(1.5f).AlignMiddle().AlignCenter()
                                            .Text(cell.Text).FontSize(7).FontColor(fg);
                                    }
                                    else
                                    {
                                        // 过道 / 空位：留白
                                        table.Cell().Height(rowH, Unit.Millimetre);
                                    }
                                }

                                // 右侧门标注
                                if (model.HasRightMargin)
                                {
                                    if (!string.IsNullOrEmpty(row.RightMarginText))
                                        table.Cell().Padding(1).Height(rowH, Unit.Millimetre)
                                            .Background(Colors.White).Border(0.8f).BorderColor("#90A8C0")
                                            .CornerRadius(1.5f).AlignMiddle().AlignCenter()
                                            .Text(row.RightMarginText).FontSize(7).FontColor("#4A6B8A");
                                    else
                                        table.Cell().Height(rowH, Unit.Millimetre);
                                }
                            }
                        });

                        if (!string.IsNullOrWhiteSpace(options.FooterNote))
                            col.Item().PaddingTop(3).AlignRight()
                                .Text(options.FooterNote).FontSize(6).FontColor("#6B7280");
                    });
                });
            }).GeneratePdf(path);
            _logger.LogInformation("PDF 座位布局导出完成: {Path}", path);
        }, cancellationToken);
    }
}

#endif
