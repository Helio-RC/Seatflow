using System;
using SeatFlow.Core.Models;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 预览座位尺寸（Grid）：按布局的真实坐标步进推导绘制宽高，避免座位互相重叠。
/// <para>
/// 背景：<see cref="SeatFlow.Core.DomainServices.SeatGeometryHelper"/> 的横向步进是
/// 桌内 <see cref="GridLayoutMetadata.IntraDeskSpacing"/>（默认 12）与桌间
/// <see cref="GridLayoutMetadata.InterDeskSpacing"/>（默认 40），而
/// <see cref="GridLayoutMetadata.HorizontalSpacing"/> 并不参与坐标累加。
/// 若按后者估尺寸（可达 51），同桌座位在预览中会严重重叠、姓名无法辨认。
/// </para>
/// </summary>
public static class PreviewSeatSize
{
    /// <summary>按网格步进计算座位绘制宽高（略小于步进以留出间隙）。</summary>
    public static (double W, double H) ForGrid(GridLayoutMetadata metadata)
    {
        var intra = metadata.IntraDeskSpacing > 0 ? metadata.IntraDeskSpacing : 12.0;
        var inter = metadata.InterDeskSpacing > 0 ? metadata.InterDeskSpacing : 40.0;
        var seatsPerDesk = metadata.SeatsPerDesk > 0 ? metadata.SeatsPerDesk : 1;
        var stepX = seatsPerDesk > 1 ? Math.Min(intra, inter) : inter;
        var stepY = metadata.VerticalSpacing > 0 ? metadata.VerticalSpacing : 36.0;
        return (Math.Clamp(stepX * 0.9, 1, 64), Math.Clamp(stepY * 0.9, 1, 40));
    }
}
