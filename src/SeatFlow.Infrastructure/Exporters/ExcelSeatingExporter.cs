using SeatFlow.Core.Exporters;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace SeatFlow.Infrastructure.Exporters;

public class ExcelSeatingExporter : ISeatingPlanExporter
{
    private readonly ILogger<ExcelSeatingExporter> _logger;

    // 正式打印风配色（低饱和浅色块 + 细线框）
    private static readonly System.Drawing.Color BorderColor = System.Drawing.Color.FromArgb(0xB0, 0xB0, 0xB0);
    private static readonly System.Drawing.Color AisleBorderColor = System.Drawing.Color.FromArgb(0xE0, 0xE0, 0xE0);
    private static readonly System.Drawing.Color UnassignedFill = System.Drawing.Color.FromArgb(0xED, 0xED, 0xED);
    private static readonly System.Drawing.Color UnassignedText = System.Drawing.Color.FromArgb(0x80, 0x80, 0x80);
    private static readonly System.Drawing.Color PodiumFill = System.Drawing.Color.FromArgb(0xDC, 0xEB, 0xF7);
    private static readonly System.Drawing.Color PodiumText = System.Drawing.Color.FromArgb(0x15, 0x65, 0xC0);
    private static readonly System.Drawing.Color AisleFill = System.Drawing.Color.FromArgb(0xF5, 0xF5, 0xF5);
    private static readonly System.Drawing.Color HeaderText = System.Drawing.Color.FromArgb(0x33, 0x33, 0x33);
    private static readonly System.Drawing.Color SubtitleText = System.Drawing.Color.FromArgb(0x66, 0x66, 0x66);

    public ExcelSeatingExporter(ILogger<ExcelSeatingExporter> logger)
    {
        _logger = logger;
        ExcelPackage.License.SetNonCommercialPersonal("SeatFlow");
    }

    public ExportFormat Format => ExportFormat.Excel;

    public async Task ExportAsync(SeatingPlan plan, string path, CancellationToken cancellationToken = default)
    {
        await ExportAsync(plan, path, new ExportOptions { Format = ExportFormat.Excel }, cancellationToken);
    }

    public async Task ExportAsync(SeatingPlan plan, string path, ExportOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            using var p = new ExcelPackage();
            BuildPlanWorkbook(p, plan, options);

            var fi = new FileInfo(path);
            await p.SaveAsAsync(fi, cancellationToken);
            _logger.LogInformation("Excel 导出完成: {Path}，{Count} 条记录", path, plan.Assignments.Count);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _logger.LogError(ex, "Excel 导出失败，回退为 CSV: {Path}", path);
            var lines = new System.Collections.Generic.List<string> { "SeatId,StudentId" };
            foreach (var kv in plan.Assignments)
                lines.Add($"{kv.Key},{(options.Anonymize ? "***" : kv.Value)}");
            await File.WriteAllLinesAsync(path, lines, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> ExportBytesAsync(SeatingPlan plan, ExportOptions options, CancellationToken cancellationToken = default)
    {
        using var p = new ExcelPackage();
        BuildPlanWorkbook(p, plan, options);
        return await p.GetAsByteArrayAsync(cancellationToken);
    }

    private static void BuildPlanWorkbook(ExcelPackage p, SeatingPlan plan, ExportOptions options)
    {
        var ws = p.Workbook.Worksheets.Add("Seating");
        ws.Cells[1, 1].Value = "SeatId";
        ws.Cells[1, 2].Value = options.Anonymize ? "StudentId (anonymized)" : "StudentId";
        int r = 2;
        foreach (var kv in plan.Assignments)
        {
            ws.Cells[r, 1].Value = kv.Key;
            ws.Cells[r, 2].Value = options.Anonymize ? "***" : kv.Value;
            r++;
        }

        if (options.IncludeMetadata)
        {
            var metaWs = p.Workbook.Worksheets.Add("Metadata");
            metaWs.Cells[1, 1].Value = "Property";
            metaWs.Cells[1, 2].Value = "Value";
            metaWs.Cells[2, 1].Value = "ExportTime";
            metaWs.Cells[2, 2].Value = DateTime.Now.ToString("O");
            metaWs.Cells[3, 1].Value = "SeatCount";
            metaWs.Cells[3, 2].Value = plan.Assignments.Count;
        }
    }

    public async Task ExportLayoutAsync(LayoutSeatingExportModel model, string path, ExportOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            using var p = new ExcelPackage();
            BuildLayoutWorkbook(p, model, options, cancellationToken);

            var fi = new FileInfo(path);
            await p.SaveAsAsync(fi, cancellationToken);
            _logger.LogInformation("Excel 布局导出完成: {Path}，{RowCount} 行", path, model.Rows.Count);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _logger.LogError(ex, "Excel 布局导出失败，回退为 CSV: {Path}", path);
            await File.WriteAllLinesAsync(path, BuildLayoutCsvFallback(model, options), cancellationToken);
        }
    }

    /// <summary>Excel 写入失败时的 CSV 回退：保持页眉与门边列结构一致。</summary>
    private static IEnumerable<string> BuildLayoutCsvFallback(LayoutSeatingExportModel model, ExportOptions options)
    {
        yield return $"# {options.HeaderTitle ?? options.Texts.SeatingChart}";
        var subtitle = options.HeaderSubtitle ?? model.LayoutName;
        if (!string.IsNullOrWhiteSpace(subtitle))
            yield return $"# {subtitle}";
        if (!string.IsNullOrWhiteSpace(options.FooterNote))
            yield return $"# {options.FooterNote}";

        bool hasLeft = model.HasLeftMargin;
        bool hasRight = model.HasRightMargin;
        foreach (var row in model.Rows)
        {
            var fields = new List<string>();
            if (hasLeft) fields.Add(row.LeftMarginText ?? "");
            fields.AddRange(row.Cells.Select(c => c.Text));
            if (hasRight) fields.Add(row.RightMarginText ?? "");
            yield return string.Join(",", fields);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> ExportLayoutBytesAsync(LayoutSeatingExportModel model, ExportOptions options, CancellationToken cancellationToken = default)
    {
        using var p = new ExcelPackage();
        BuildLayoutWorkbook(p, model, options, cancellationToken);
        return await p.GetAsByteArrayAsync(cancellationToken);
    }

    private static void BuildLayoutWorkbook(ExcelPackage p, LayoutSeatingExportModel model, ExportOptions options, CancellationToken ct)
    {
        var ws = p.Workbook.Worksheets.Add("Seating");

        int dataCols = model.Rows.Count > 0 ? model.Rows.Max(r => r.Cells.Count) : 1;
        if (dataCols == 0) dataCols = 1;
        int leftMargin = model.HasLeftMargin ? 1 : 0;
        int rightMargin = model.HasRightMargin ? 1 : 0;
        int totalCols = dataCols + leftMargin + rightMargin;
        int dataStart = 1 + leftMargin;

        BuildHeader(ws, model, options, totalCols);

        // 列统计：过道列（窄）与文本长度（估算列宽）
        var colSeatCount = new int[dataCols];
        var colAisleCount = new int[dataCols];
        var colMaxLen = new int[dataCols];
        var podiumMerges = new List<(int Row, int Start, int Span, string Text)>();

        int r = 4;
        int rowIndex = 0;
        foreach (var row in model.Rows)
        {
            if (++rowIndex % 30 == 0)
                ct.ThrowIfCancellationRequested();

            bool isFullAisleRow = row.Cells.Count > 0 && row.Cells.All(cell => cell.IsAisle);
            bool hasPodium = row.Cells.Any(c => c.IsPodium);

            if (leftMargin > 0)
            {
                var doorCell = ws.Cells[r, 1];
                doorCell.Value = row.LeftMarginText ?? "";
                StyleDoorCell(doorCell, row.LeftMarginText);
            }

            for (int i = 0; i < row.Cells.Count && i < dataCols; i++)
            {
                var cell = row.Cells[i];
                var xc = ws.Cells[r, dataStart + i];
                xc.Value = cell.Text;
                StyleDataCell(xc, cell, isFullAisleRow);

                if (cell.IsSeat)
                    colSeatCount[i]++;
                else if (cell.IsAisle)
                    colAisleCount[i]++;
                if (!string.IsNullOrEmpty(cell.Text))
                    colMaxLen[i] = Math.Max(colMaxLen[i], VisualLength(cell.Text));

                if (cell.IsPodium)
                {
                    int span = Math.Min(3, dataCols);
                    int start = Math.Clamp(i - (span - 1) / 2, 0, dataCols - span);
                    podiumMerges.Add((r, start, span, cell.Text));
                }
            }

            if (rightMargin > 0)
            {
                var doorCell = ws.Cells[r, dataStart + dataCols];
                doorCell.Value = row.RightMarginText ?? "";
                StyleDoorCell(doorCell, row.RightMarginText);
            }

            ws.Row(r).Height = isFullAisleRow ? 8 : hasPodium ? 26 : 30;
            r++;
        }

        // 讲台合并（居中宽块）：文本与样式写到合并区左上格，否则合并后不可见
        foreach (var (mergeRow, start, span, text) in podiumMerges)
        {
            for (int c = start; c < start + span; c++)
                ws.Cells[mergeRow, dataStart + c].Value = null;

            var range = ws.Cells[mergeRow, dataStart + start, mergeRow, dataStart + start + span - 1];
            range.Merge = true;

            var topLeft = ws.Cells[mergeRow, dataStart + start];
            topLeft.Value = text;
            StylePodiumCell(topLeft);

            range.Style.Border.BorderAround(ExcelBorderStyle.Thin, BorderColor);
        }

        var colIsAisle = new bool[dataCols];
        for (int i = 0; i < dataCols; i++)
            colIsAisle[i] = colAisleCount[i] > 0 && colSeatCount[i] == 0;

        ApplyColumnWidths(ws, dataCols, dataStart, leftMargin, rightMargin, colIsAisle, colMaxLen);
        ApplyPrintSettings(ws);

        if (options.IncludeMetadata)
            BuildMetadataSheet(p, model, options);
    }

    private static void BuildHeader(ExcelWorksheet ws, LayoutSeatingExportModel model, ExportOptions options, int totalCols)
    {
        var texts = options.Texts;

        ws.Cells[1, 1].Value = options.HeaderTitle ?? texts.SeatingChart;
        ws.Cells[1, 1, 1, totalCols].Merge = true;
        ws.Cells[1, 1].Style.Font.Bold = true;
        ws.Cells[1, 1].Style.Font.Size = 16;
        ws.Cells[1, 1].Style.Font.Color.SetColor(HeaderText);
        ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        ws.Cells[1, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        ws.Row(1).Height = 28;

        var subtitle = options.HeaderSubtitle ?? model.LayoutName;
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            ws.Cells[2, 1].Value = subtitle;
            ws.Cells[2, 1, 2, totalCols].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 9;
            ws.Cells[2, 1].Style.Font.Color.SetColor(SubtitleText);
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            ws.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        }
        ws.Row(2).Height = 15;
        ws.Row(3).Height = 6;
    }

    private static void StyleDataCell(ExcelRange cell, ExportCell data, bool isFullAisleRow)
    {
        cell.Style.Font.Size = 10;
        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        cell.Style.WrapText = true;

        var border = data.IsAisle || isFullAisleRow ? AisleBorderColor : BorderColor;
        cell.Style.Border.BorderAround(ExcelBorderStyle.Thin, border);

        if (data.IsUnassigned)
        {
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(UnassignedFill);
            cell.Style.Font.Color.SetColor(UnassignedText);
        }
        else if (data.IsPodium)
        {
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(PodiumFill);
            cell.Style.Font.Bold = true;
            cell.Style.Font.Color.SetColor(PodiumText);
        }
        else if (data.IsAisle || isFullAisleRow)
        {
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(AisleFill);
        }
    }

    private static void StylePodiumCell(ExcelRange cell)
    {
        cell.Style.Font.Size = 10;
        cell.Style.Font.Bold = true;
        cell.Style.Font.Color.SetColor(PodiumText);
        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
        cell.Style.Fill.BackgroundColor.SetColor(PodiumFill);
        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        cell.Style.WrapText = true;
    }

    private static void StyleDoorCell(ExcelRange cell, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;

        cell.Style.Font.Size = 9;
        cell.Style.Font.Color.SetColor(SubtitleText);
        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        cell.Style.WrapText = true;
        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
        cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.White);
        cell.Style.Border.BorderAround(ExcelBorderStyle.Dashed, BorderColor);
    }

    private static void ApplyColumnWidths(
        ExcelWorksheet ws,
        int dataCols,
        int dataStart,
        int leftMargin,
        int rightMargin,
        bool[] colIsAisle,
        int[] colMaxLen)
    {
        for (int i = 0; i < dataCols; i++)
        {
            double width;
            if (colIsAisle[i] && colMaxLen[i] == 0)
                width = 3;
            else
                width = Math.Clamp(colMaxLen[i] * 1.2 + 2, 8, 16);
            ws.Column(dataStart + i).Width = width;
        }

        if (leftMargin > 0)
            ws.Column(1).Width = 5;
        if (rightMargin > 0)
            ws.Column(dataStart + dataCols).Width = 5;
    }

    private static void ApplyPrintSettings(ExcelWorksheet ws)
    {
        ws.View.ShowGridLines = false;
        ws.View.FreezePanes(4, 1);
        ws.PrinterSettings.ShowGridLines = false;
        ws.PrinterSettings.Orientation = eOrientation.Landscape;
        ws.PrinterSettings.FitToPage = true;
        ws.PrinterSettings.FitToWidth = 1;
        ws.PrinterSettings.FitToHeight = 0;
        ws.PrinterSettings.RepeatRows = new ExcelAddress(1, 1, 2, ExcelPackage.MaxColumns);
    }

    private static void BuildMetadataSheet(ExcelPackage p, LayoutSeatingExportModel model, ExportOptions options)
    {
        var metaWs = p.Workbook.Worksheets.Add("Metadata");
        metaWs.Cells[1, 1].Value = "Property";
        metaWs.Cells[1, 2].Value = "Value";
        metaWs.Cells[2, 1].Value = "ExportTime";
        metaWs.Cells[2, 2].Value = DateTime.Now.ToString("O");
        metaWs.Cells[3, 1].Value = "LayoutName";
        metaWs.Cells[3, 2].Value = model.LayoutName;
        metaWs.Cells[4, 1].Value = "Perspective";
        metaWs.Cells[4, 2].Value = options.Perspective.ToString();
        metaWs.Cells[5, 1].Value = "Subtitle";
        metaWs.Cells[5, 2].Value = options.HeaderSubtitle ?? "";
    }

    private static int VisualLength(string text)
        => text.Sum(ch => ch > 0x2E80 ? 2 : 1);
}
