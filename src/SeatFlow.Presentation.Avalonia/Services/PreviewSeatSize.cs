using System;
using SeatFlow.Core.Models;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// Grid 预览座位尺寸与坐标放大系数。
/// <para>
/// 背景：<see cref="SeatFlow.Core.DomainServices.SeatGeometryHelper"/> 的横向步进是
/// 桌内 <see cref="GridLayoutMetadata.IntraDeskSpacing"/>（默认 12）与桌间
/// <see cref="GridLayoutMetadata.InterDeskSpacing"/>（默认 32），而
/// <see cref="GridLayoutMetadata.HorizontalSpacing"/> 并不参与坐标累加。
/// 若直接按原始坐标绘制，座位尺寸只能取步进的一小部分（如 10px），
/// 小于文字所需宽度，放大后也只能看到省略号。
/// </para>
/// <para>
/// 因此预览采用「可读尺寸 + 坐标等比例放大」：座位固定为可容纳姓名的
/// <see cref="W"/>×<see cref="H"/>（板面单位），坐标按 <see cref="FactorX"/>/
/// <see cref="FactorY"/> 放大到步进与座位尺寸匹配，保证不重叠且姓名可读。
/// </para>
/// </summary>
public readonly record struct GridPreviewMetrics(double W, double H, double FactorX, double FactorY);

/// <summary>预览座位指标计算（会场编辑与快照预览共用）。</summary>
public static class PreviewSeatSize
{
    /// <summary>座位绘制目标宽度（板面单位，按 10 号字可容纳 3–4 字/行 × 2 行）。</summary>
    public const double SeatWidth = 40;

    /// <summary>座位绘制目标高度（板面单位）。</summary>
    public const double SeatHeight = 23;

    /// <summary>按网格步进计算座位尺寸与坐标放大系数（步进小于座位时按比例放大坐标）。</summary>
    public static GridPreviewMetrics ForGrid(GridLayoutMetadata metadata)
    {
        var intra = metadata.IntraDeskSpacing > 0 ? metadata.IntraDeskSpacing : 12.0;
        var inter = metadata.InterDeskSpacing > 0 ? metadata.InterDeskSpacing : 32.0;
        var seatsPerDesk = metadata.SeatsPerDesk > 0 ? metadata.SeatsPerDesk : 1;
        var stepX = seatsPerDesk > 1 ? Math.Min(intra, inter) : inter;
        var stepY = metadata.VerticalSpacing > 0 ? metadata.VerticalSpacing : 36.0;

        var factorX = Math.Max(1, (SeatWidth / 0.9) / stepX);
        var factorY = Math.Max(1, (SeatHeight / 0.9) / stepY);
        return new GridPreviewMetrics(SeatWidth, SeatHeight, factorX, factorY);
    }
}
