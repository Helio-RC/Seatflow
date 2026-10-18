using System;
using System.Collections.Generic;
using System.Linq;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.Lang;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// Grid 会场预览构建：由 <see cref="GridLayoutMetadata"/> 生成与会场页一致的
/// <see cref="SeatLayoutSnapshot"/>（复用 <see cref="GridVisualGeometryBuilder"/> 的视觉几何）。
/// </summary>
public static class VenueGridPreviewBuilder
{
    /// <summary>
    /// 构建 Grid 预览快照。
    /// </summary>
    /// <param name="metadata">网格元数据（EmptyPositions 应已按当前草稿过滤）。</param>
    /// <param name="selectedDisabledPositions">禁用座位草稿中处于选中态的坐标（用于高亮）。</param>
    /// <param name="extraOverlays">
    /// 追加的覆盖物（如门），X/Y 使用元数据坐标并按几何系数缩放；宽高保持不变。
    /// </param>
    public static SeatLayoutSnapshot Build(
        GridLayoutMetadata metadata,
        IReadOnlySet<(int Row, int Column)>? selectedDisabledPositions = null,
        IReadOnlyList<BoardOverlay>? extraOverlays = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var selected = selectedDisabledPositions ?? new HashSet<(int Row, int Column)>();
        var geometry = GridVisualGeometryBuilder.Build(metadata);
        var seats = new List<SeatVisual>(geometry.Seats.Count);

        foreach (var seat in geometry.Seats)
        {
            bool isPicked = selected.Contains((seat.Row, seat.Column));
            if (seat.IsDisabled)
            {
                seats.Add(new SeatVisual(
                    seat.SeatId, seat.X, seat.Y, seat.Width, seat.Height,
                    IsDisabled: true,
                    SeatLabel: string.Format(Resources.Venue_GridDisabledFmt, seat.Row, seat.Column),
                    Row: seat.Row, Column: seat.Column, IsSelected: isPicked));
                continue;
            }

            int deskNum = ((seat.Column - 1) / Math.Max(1, metadata.SeatsPerDesk)) + 1;
            seats.Add(new SeatVisual(
                seat.SeatId, seat.X, seat.Y, seat.Width, seat.Height,
                IsOccupied: true,
                SeatLabel: string.Format(Resources.Venue_GridLabelFmt, seat.Row, seat.Column, deskNum),
                Row: seat.Row, Column: seat.Column, IsSelected: isPicked));
        }

        var overlays = new List<BoardOverlay>();
        if (metadata.HasPodium && metadata.PodiumWidth > 0 && metadata.PodiumHeight > 0 && seats.Count > 0)
        {
            double gridLeft = seats.Min(s => s.X);
            double gridRight = seats.Max(s => s.X + s.Width);
            double podiumW = metadata.PodiumWidth * geometry.FactorX;
            double podiumX = ((gridLeft + gridRight) / 2) - (podiumW / 2);
            double podiumH = metadata.PodiumHeight * geometry.FactorY;
            double podiumY = (metadata.OriginY - metadata.PodiumHeight - metadata.VerticalSpacing) * geometry.FactorY;
            overlays.Add(new BoardOverlay(
                podiumX, podiumY, podiumW, podiumH, Resources.Freeform_Podium));
        }

        if (extraOverlays is not null)
        {
            overlays.AddRange(extraOverlays.Select(o => o with
            {
                X = o.X * geometry.FactorX,
                Y = o.Y * geometry.FactorY,
            }));
        }

        return SeatLayoutSnapshotNormalizer.Normalize(seats, overlays);
    }
}
