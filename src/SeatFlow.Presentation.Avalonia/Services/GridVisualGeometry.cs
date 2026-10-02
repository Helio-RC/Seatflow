using System;
using System.Collections.Generic;
using System.Linq;
using SeatFlow.Core.DomainServices;
using SeatFlow.Core.Models;
using SeatFlow.Infrastructure.Layouts;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>Grid 视觉座位几何（板面坐标，已放大到可读尺寸）。</summary>
/// <param name="SeatId">座位 ID（禁用位为 <c>disabled-r{row}c{col}</c>）。</param>
/// <param name="Row">行号（1 起）。</param>
/// <param name="Column">列号（1 起）。</param>
/// <param name="X">左上角 X（板面单位）。</param>
/// <param name="Y">左上角 Y（板面单位）。</param>
/// <param name="Width">座位宽。</param>
/// <param name="Height">座位高。</param>
/// <param name="IsDisabled">是否为「禁用座位」占位（虚线绘制）。</param>
public readonly record struct GridVisualSeat(
    string SeatId,
    int Row,
    int Column,
    double X,
    double Y,
    double Width,
    double Height,
    bool IsDisabled);

/// <summary>Grid 视觉几何结果：座位列表 + 坐标放大系数（供讲台/门等叠加层按同一比例缩放）。</summary>
/// <param name="Seats">座位（含禁用位）列表。</param>
/// <param name="FactorX">X 坐标放大系数。</param>
/// <param name="FactorY">Y 坐标放大系数。</param>
/// <param name="SeatWidth">可读座位宽。</param>
/// <param name="SeatHeight">可读座位高。</param>
public sealed record GridVisualGeometry(
    IReadOnlyList<GridVisualSeat> Seats,
    double FactorX,
    double FactorY,
    double SeatWidth,
    double SeatHeight);

/// <summary>
/// Grid 布局的统一视觉几何构建：会场预览与排座工作台共用，保证两处的桌间距/过道宽度
/// 呈现一致。
/// <para>
/// 规则：同桌间距按 0.8 视觉压缩（沿用会场预览既有约定，不改动持久化数据），
/// 再按 <see cref="PreviewSeatSize"/> 把坐标等比例放大到可读座位尺寸；
/// 桌间距与过道宽度语义与 <see cref="SeatGeometryHelper"/> 完全一致。
/// </para>
/// </summary>
public static class GridVisualGeometryBuilder
{
    /// <summary>按元数据构建统一的 Grid 视觉几何。</summary>
    public static GridVisualGeometry Build(GridLayoutMetadata metadata)
    {
        var visualMeta = Clone(metadata);
        visualMeta.IntraDeskSpacing = metadata.IntraDeskSpacing * 0.8;

        var layout = GridLayoutBuilder.BuildGrid(visualMeta);
        var metrics = PreviewSeatSize.ForGrid(visualMeta);
        var seats = new List<GridVisualSeat>(layout.Seats.Count + (metadata.EmptyPositions?.Count ?? 0));

        foreach (var seat in layout.Seats.OfType<GridSeat>())
        {
            var (x, y) = SeatGeometryHelper.GetPosition(seat, visualMeta);
            seats.Add(new GridVisualSeat(
                seat.Id, seat.Row, seat.Column,
                x * metrics.FactorX, y * metrics.FactorY,
                metrics.W, metrics.H,
                IsDisabled: false));
        }

        foreach (var empty in metadata.EmptyPositions ?? [])
        {
            var virtualSeat = new GridSeat { Row = empty.Row, Column = empty.Column };
            var (x, y) = SeatGeometryHelper.GetPosition(virtualSeat, visualMeta);
            seats.Add(new GridVisualSeat(
                $"disabled-r{empty.Row}c{empty.Column}", empty.Row, empty.Column,
                x * metrics.FactorX, y * metrics.FactorY,
                metrics.W, metrics.H,
                IsDisabled: true));
        }

        return new GridVisualGeometry(seats, metrics.FactorX, metrics.FactorY, metrics.W, metrics.H);
    }

    /// <summary>复制 Grid 元数据（仅视觉压缩用，避免修改调用方实例）。</summary>
    public static GridLayoutMetadata Clone(GridLayoutMetadata meta) => new()
    {
        Rows = meta.Rows,
        Columns = meta.Columns,
        OriginX = meta.OriginX,
        OriginY = meta.OriginY,
        SeatsPerDesk = meta.SeatsPerDesk,
        IntraDeskSpacing = meta.IntraDeskSpacing,
        InterDeskSpacing = meta.InterDeskSpacing,
        HorizontalSpacing = meta.HorizontalSpacing,
        VerticalSpacing = meta.VerticalSpacing,
        AisleAfterColumns = meta.AisleAfterColumns,
        AisleAfterRows = meta.AisleAfterRows,
        AisleWidth = meta.AisleWidth,
        ColumnRowCounts = meta.ColumnRowCounts,
        FrontRowCount = meta.FrontRowCount,
        HasPodium = meta.HasPodium,
        PodiumWidth = meta.PodiumWidth,
        PodiumHeight = meta.PodiumHeight,
        HasFrontDoor = meta.HasFrontDoor,
        EmptyPositions = meta.EmptyPositions,
    };
}
