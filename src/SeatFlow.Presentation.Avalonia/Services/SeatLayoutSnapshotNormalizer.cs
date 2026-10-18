using System;
using System.Collections.Generic;
using System.Linq;
using SeatFlow.Presentation.Avalonia.Controls;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 座位布局快照归一化：把任意布局的座位/覆盖物平移到正坐标，并计算板面尺寸。
/// 会场预览与 OOBE 网格预览共用，保证 24px 内边距与板面尺寸一致。
/// </summary>
public static class SeatLayoutSnapshotNormalizer
{
    /// <summary>将内容平移至正坐标并计算板面尺寸（SeatingCanvas 会自动适配视口）。</summary>
    public static SeatLayoutSnapshot Normalize(
        IReadOnlyList<SeatVisual> seats,
        IReadOnlyList<BoardOverlay> overlays)
    {
        ArgumentNullException.ThrowIfNull(seats);
        ArgumentNullException.ThrowIfNull(overlays);

        if (seats.Count == 0 && overlays.Count == 0)
            return SeatLayoutSnapshot.Empty;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        void Acc(double x, double y, double w, double h)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + w);
            maxY = Math.Max(maxY, y + h);
        }

        foreach (var s in seats)
            Acc(s.X, s.Y, s.Width, s.Height);
        foreach (var o in overlays)
            Acc(o.X, o.Y, o.Width, o.Height);

        const double padding = 24;
        double dx = padding - minX;
        double dy = padding - minY;

        var movedSeats = seats.Select(s => s with { X = s.X + dx, Y = s.Y + dy }).ToList();
        var movedOverlays = overlays.Select(o => o with { X = o.X + dx, Y = o.Y + dy }).ToList();

        return new SeatLayoutSnapshot(
            movedSeats,
            Math.Max(320, maxX + dx + padding),
            Math.Max(240, maxY + dy + padding),
            0,
            movedOverlays);
    }
}
