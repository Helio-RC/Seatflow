#if !BROWSER
using SeatFlow.Core.Exporters;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace SeatFlow.Infrastructure.Exporters;

public class ImageSeatingExporter(ILogger<ImageSeatingExporter>? logger = null) : ISeatingPlanExporter
{
    private const int SeatCellWidth = 88;
    private const int SeatRowHeight = 34;
    private const int AisleColWidth = 24;
    private const int AisleRowHeight = 12;
    private const int MarginColWidth = 56;
    private const int Padding = 24;
    private const int MaxDimension = 2000;

    private const float TitleSize = 18f;
    private const float SubtitleSize = 10.5f;
    private const float SeatTextSize = 11.5f;
    private const float DoorTextSize = 10f;
    private const float FooterTextSize = 9.5f;

    // 预览海报风配色（与应用内布局预览同色系）
    private static readonly SKColor SeatFill = new(0xE9, 0xED, 0xFB);
    private static readonly SKColor SeatBorder = new(0xA9, 0xB4, 0xDE);
    private static readonly SKColor SeatText = new(0x2A, 0x35, 0x4A);
    private static readonly SKColor UnassignedFill = new(0xF2, 0xF2, 0xF2);
    private static readonly SKColor UnassignedBorder = new(0xCC, 0xCC, 0xCC);
    private static readonly SKColor UnassignedText = new(0x8A, 0x8A, 0x8A);
    private static readonly SKColor PodiumFill = new(0xE3, 0xF2, 0xFD);
    private static readonly SKColor PodiumBorder = new(0x90, 0xCA, 0xF9);
    private static readonly SKColor PodiumText = new(0x15, 0x65, 0xC0);
    private static readonly SKColor DoorBorder = new(0x90, 0xA8, 0xC0);
    private static readonly SKColor DoorText = new(0x4A, 0x6B, 0x8A);
    private static readonly SKColor TitleColor = new(0x1F, 0x2A, 0x44);
    private static readonly SKColor SubtitleColor = new(0x6B, 0x72, 0x80);
    private static readonly SKColor SeparatorColor = new(0xE3, 0xE6, 0xEB);

    private readonly ILogger<ImageSeatingExporter> _logger = logger ?? NullLogger<ImageSeatingExporter>.Instance;

    /// <summary>跨平台 CJK 字体：优先嵌入的 Noto Sans SC，其次系统匹配，最后默认字体。</summary>
    private static readonly SKTypeface CjkTypeface = ResolveCjkTypeface();

    private static SKTypeface ResolveCjkTypeface()
    {
        try
        {
            using var stream = typeof(ImageSeatingExporter).Assembly
                .GetManifestResourceStream("SeatFlow.Infrastructure.Assets.NotoSansSC-Regular.otf");
            if (stream is not null)
            {
                var embedded = SKTypeface.FromStream(stream);
                if (embedded is not null)
                    return embedded;
            }
        }
        catch
        {
            // 嵌入字体不可用时回退到系统字体
        }

        var fm = SKFontManager.Default;
        return fm.MatchCharacter('中') ?? SKTypeface.Default;
    }

    public ExportFormat Format => ExportFormat.Png;

    public Task ExportAsync(SeatingPlan plan, string path, CancellationToken cancellationToken = default)
        => ExportAsync(plan, path, new ExportOptions { Format = ExportFormat.Png }, cancellationToken);

    public Task ExportAsync(SeatingPlan plan, string path, ExportOptions options, CancellationToken cancellationToken = default)
        => Task.CompletedTask; // 图片导出使用 ExportLayoutAsync

    public Task ExportLayoutAsync(LayoutSeatingExportModel model, string path, ExportOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("图片座位布局导出开始：{Path}（{RowCount} 行）", path, model.Rows.Count);

        if (model.Rows.Count == 0)
            _logger.LogDebug("图片布局导出：模型无行，输出仅含页眉的预览图");

        int dataCols = model.Rows.Count > 0 ? Math.Max(1, model.Rows.Max(r => r.Cells.Count)) : 0;

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
            colWidths[i] = colAisleCount[i] > 0 && colSeatCount[i] == 0 ? AisleColWidth : SeatCellWidth;

        var rowHeights = new float[model.Rows.Count];
        for (int i = 0; i < model.Rows.Count; i++)
        {
            var row = model.Rows[i];
            bool isAisleRow = row.Cells.Count > 0 && row.Cells.All(c => c.IsAisle);
            rowHeights[i] = isAisleRow ? AisleRowHeight
                : row.Cells.Any(c => c.IsPodium) ? SeatRowHeight + 4
                : SeatRowHeight;
        }

        float leftMargin = model.HasLeftMargin ? MarginColWidth : 0;
        float rightMargin = model.HasRightMargin ? MarginColWidth : 0;
        float tableWidth = Math.Max(leftMargin + colWidths.Sum() + rightMargin, model.Rows.Count == 0 ? 240f : 0f);
        float tableHeight = rowHeights.Sum();

        float headerHeight = 66;
        float footerHeight = string.IsNullOrWhiteSpace(options.FooterNote) ? 14 : 32;

        int width = (int)Math.Ceiling(Padding * 2 + tableWidth);
        int height = (int)Math.Ceiling(Padding * 2 + headerHeight + tableHeight + footerHeight);

        float scale = Math.Min(1f, MaxDimension / (float)Math.Max(width, height));
        if (scale < 1f)
        {
            width = Math.Max(1, (int)(width * scale));
            height = Math.Max(1, (int)(height * scale));
        }

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        if (scale < 1f)
            canvas.Scale(scale);

        float y = Padding;
        DrawHeader(canvas, model, options, Padding, tableWidth, ref y);
        float tableTop = y;

        DrawTable(canvas, model, colWidths, rowHeights, leftMargin, ref y,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(options.FooterNote))
        {
            var footerRect = new SKRect(Padding, tableTop + tableHeight + 10, Padding + tableWidth, tableTop + tableHeight + 28);
            DrawCenteredText(canvas, options.FooterNote, footerRect, SubtitleColor, FooterTextSize);
        }

        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            using var stream = File.OpenWrite(path);
            data.SaveTo(stream);
            _logger.LogInformation("图片座位布局导出完成: {Path}（{Width}x{Height}）", path, width, height);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "写入图片文件失败: {Path}", path);
            throw new IOException($"无法写入图片文件，文件可能正在被其他程序占用: {path}", ex);
        }

        return Task.CompletedTask;
    }

    private static void DrawHeader(
        SKCanvas canvas,
        LayoutSeatingExportModel model,
        ExportOptions options,
        float x,
        float width,
        ref float y)
    {
        var title = options.HeaderTitle ?? options.Texts.SeatingChart;
        DrawCenteredText(canvas, title, new SKRect(x, y, x + width, y + 28), TitleColor, TitleSize);
        y += 30;

        var subtitle = options.HeaderSubtitle ?? model.LayoutName;
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            DrawCenteredText(canvas, subtitle, new SKRect(x, y, x + width, y + 18), SubtitleColor, SubtitleSize);
            y += 20;
        }

        using var separator = new SKPaint
        {
            Color = SeparatorColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };
        canvas.DrawLine(x, y + 3, x + width, y + 3, separator);
        y += 12;
    }

    private static void DrawTable(
        SKCanvas canvas,
        LayoutSeatingExportModel model,
        float[] colWidths,
        float[] rowHeights,
        float leftMargin,
        ref float y,
        CancellationToken ct)
    {
        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };
        using var dashedBorderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash([5f, 4f], 0)
        };

        bool hasRightMargin = model.HasRightMargin;

        int rowCounter = 0;
        foreach (var row in model.Rows)
        {
            if (++rowCounter % 30 == 0)
                ct.ThrowIfCancellationRequested();

            float rowH = rowHeights[rowCounter - 1];
            float tableX = Padding + leftMargin;
            float x = tableX;

            for (int i = 0; i < row.Cells.Count; i++)
            {
                var cell = row.Cells[i];
                float colW = i < colWidths.Length ? colWidths[i] : SeatCellWidth;
                var rect = new SKRect(x, y, x + colW, y + rowH);

                if (cell.IsPodium)
                {
                    // 讲台按列合并绘制（居中宽块），在下方统一处理
                }
                else if (!cell.IsAisle && cell.IsSeat)
                {
                    DrawSeatCard(canvas, rect, cell, fillPaint, borderPaint);
                }

                x += colW;
            }

            // 讲台宽块
            int podiumIndex = row.Cells.FindIndex(c => c.IsPodium);
            if (podiumIndex >= 0)
            {
                int span = Math.Min(3, row.Cells.Count);
                int start = Math.Clamp(podiumIndex - (span - 1) / 2, 0, Math.Max(0, row.Cells.Count - span));
                float spanX = tableX;
                for (int i = 0; i < start; i++)
                    spanX += colWidths[i];
                float spanW = 0;
                for (int i = start; i < start + span && i < colWidths.Length; i++)
                    spanW += colWidths[i];

                var podiumRect = new SKRect(spanX, y + 2, spanX + spanW, y + rowH - 2);
                fillPaint.Color = PodiumFill;
                canvas.DrawRoundRect(podiumRect, 6, 6, fillPaint);
                borderPaint.Color = PodiumBorder;
                canvas.DrawRoundRect(podiumRect, 6, 6, borderPaint);
                DrawCenteredText(canvas, row.Cells[podiumIndex].Text, podiumRect, PodiumText, SeatTextSize);
            }

            // 门标注（左右边列，按行对齐）
            if (leftMargin > 0 && !string.IsNullOrEmpty(row.LeftMarginText))
                DrawDoorPlate(canvas, new SKRect(Padding + 4, y + 5, Padding + leftMargin - 4, y + rowH - 5), row.LeftMarginText, fillPaint, dashedBorderPaint);

            if (hasRightMargin && !string.IsNullOrEmpty(row.RightMarginText))
            {
                float rightMargin = MarginColWidth;
                float rightX = tableX + colWidths.Sum();
                DrawDoorPlate(canvas, new SKRect(rightX + 4, y + 5, rightX + rightMargin - 4, y + rowH - 5), row.RightMarginText, fillPaint, dashedBorderPaint);
            }

            y += rowH;
        }
    }

    private static void DrawSeatCard(SKCanvas canvas, SKRect rect, ExportCell cell, SKPaint fillPaint, SKPaint borderPaint)
    {
        var card = new SKRect(rect.Left + 2, rect.Top + 2, rect.Right - 2, rect.Bottom - 2);
        bool unassigned = cell.IsUnassigned;

        fillPaint.Color = unassigned ? UnassignedFill : SeatFill;
        canvas.DrawRoundRect(card, 5, 5, fillPaint);
        borderPaint.Color = unassigned ? UnassignedBorder : SeatBorder;
        canvas.DrawRoundRect(card, 5, 5, borderPaint);

        DrawCenteredText(canvas, cell.Text, card,
            unassigned ? UnassignedText : SeatText, SeatTextSize);
    }

    private static void DrawDoorPlate(SKCanvas canvas, SKRect rect, string text, SKPaint fillPaint, SKPaint dashedBorderPaint)
    {
        fillPaint.Color = SKColors.White;
        canvas.DrawRoundRect(rect, 5, 5, fillPaint);
        dashedBorderPaint.Color = DoorBorder;
        canvas.DrawRoundRect(rect, 5, 5, dashedBorderPaint);
        DrawCenteredText(canvas, text, rect, DoorText, DoorTextSize);
    }

    private static void DrawCenteredText(SKCanvas canvas, string text, SKRect rect, SKColor color, float preferredSize, float minSize = 7f)
    {
        if (string.IsNullOrEmpty(text)) return;

        using var paint = new SKPaint { Color = color, IsAntialias = true };
        float size = preferredSize;
        using var font = new SKFont(CjkTypeface, size);
        float measured = font.MeasureText(text, out var bounds);

        while (measured > rect.Width - 6 && size > minSize)
        {
            size -= 1f;
            font.Size = size;
            measured = font.MeasureText(text, out bounds);
        }

        float baseline = rect.MidY - (bounds.Top + bounds.Bottom) / 2f;
        canvas.DrawText(text, rect.MidX, baseline, SKTextAlign.Center, font, paint);
    }
}
#endif
