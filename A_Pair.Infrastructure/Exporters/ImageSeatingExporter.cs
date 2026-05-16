using A_Pair.Core.Exporters;
using A_Pair.Core.Models;
using A_Pair.Core.Workspace;
using SkiaSharp;

namespace A_Pair.Infrastructure.Exporters;

public class ImageSeatingExporter : ISeatingPlanExporter
{
    private const int CellWidth = 76;
    private const int CellHeight = 32;
    private const int AisleRowHeight = 16;
    private const int Margin = 20;
    private const float TextSize = 11;

    public ExportFormat Format => ExportFormat.Png;

    public Task ExportAsync (SeatingPlan plan , string path , CancellationToken cancellationToken = default)
        => ExportAsync(plan , path , new ExportOptions { Format = ExportFormat.Png } , cancellationToken);

    public Task ExportAsync (SeatingPlan plan , string path , ExportOptions options , CancellationToken cancellationToken = default)
        => Task.CompletedTask; // 图片导出使用 ExportLayoutAsync

    public Task ExportLayoutAsync (LayoutSeatingExportModel model , string path , ExportOptions options , CancellationToken cancellationToken = default)
    {
        if (model.Rows.Count == 0) return Task.CompletedTask;

        int maxCols = model.Rows.Max(r => r.Cells.Count);
        int width = maxCols * CellWidth + Margin * 2;
        int height = model.Rows.Count * CellHeight + Margin * 2;

        using var bitmap = new SKBitmap(width , height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var borderPaint = new SKPaint
        {
            Color = SKColors.LightGray , Style = SKPaintStyle.Stroke , StrokeWidth = 0.5f , IsAntialias = true
        };
        using var seatFill = new SKPaint { Color = SKColors.White , Style = SKPaintStyle.Fill };
        using var aisleFill = new SKPaint { Color = new SKColor(0xE0 , 0xE0 , 0xE0) , Style = SKPaintStyle.Fill };
        using var podiumFill = new SKPaint { Color = new SKColor(0xE3 , 0xF2 , 0xFD) , Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black , TextSize = TextSize , IsAntialias = true , SubpixelText = true
        };

        float y = Margin;
        foreach (var row in model.Rows)
        {
            bool isAisleRow = row.Cells.Count > 0 && row.Cells.All(c => c.IsAisle);
            float rowH = isAisleRow ? AisleRowHeight : CellHeight;
            float x = Margin;

            foreach (var cell in row.Cells)
            {
                var rect = new SKRect(x , y , x + CellWidth , y + rowH);

                var fill = cell.IsPodium ? podiumFill
                    : (cell.IsAisle || isAisleRow) ? aisleFill
                    : seatFill;
                canvas.DrawRect(rect , fill);
                canvas.DrawRect(rect , borderPaint);

                if (!string.IsNullOrEmpty(cell.Text))
                {
                    float textY = y + rowH / 2 + TextSize / 3;
                    canvas.DrawText(cell.Text , x + 3 , textY , textPaint);
                }

                x += CellWidth;
            }

            y += rowH;
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png , 90);
        using var stream = System.IO.File.OpenWrite(path);
        data.SaveTo(stream);

        return Task.CompletedTask;
    }
}
