using FluentAssertions;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 预览座位指标测试（用户实测：快照预览座位重叠/放大只见省略号）：
/// Grid 预览使用可读座位尺寸（40×23），并对坐标按因子放大，使步进不小于座位。
/// </summary>
public class PreviewSeatSizeTests
{
    [Fact]
    public void Grid预览指标_密排同桌_放大坐标且座位不重叠()
    {
        var meta = new GridLayoutMetadata
        {
            SeatsPerDesk = 2,
            IntraDeskSpacing = 12,
            InterDeskSpacing = 40,
            VerticalSpacing = 56,
        };

        var metrics = PreviewSeatSize.ForGrid(meta);

        metrics.W.Should().Be(PreviewSeatSize.SeatWidth);
        metrics.H.Should().Be(PreviewSeatSize.SeatHeight);
        metrics.FactorX.Should().BeGreaterThan(1, "同桌步进 12 小于可读座位宽，需放大坐标");
        // 折算回原始坐标：座位占用的步进不超过真实步进 → 不重叠
        (metrics.W / metrics.FactorX).Should().BeLessThanOrEqualTo(12);
        (metrics.H / metrics.FactorY).Should().BeLessThanOrEqualTo(56);
    }

    [Fact]
    public void Grid预览指标_宽间距_不放大坐标()
    {
        var meta = new GridLayoutMetadata
        {
            SeatsPerDesk = 1,
            IntraDeskSpacing = 12,
            InterDeskSpacing = 80,
            VerticalSpacing = 120,
        };

        var metrics = PreviewSeatSize.ForGrid(meta);

        metrics.FactorX.Should().Be(1, "桌间距 80 已大于座位宽");
        metrics.FactorY.Should().Be(1, "行距 120 已大于座位高");
        metrics.W.Should().BeLessThan(80);
    }
}
