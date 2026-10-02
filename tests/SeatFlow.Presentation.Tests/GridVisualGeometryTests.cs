using FluentAssertions;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// Grid 视觉几何统一构建测试：会场预览与排座工作台共用同一几何，
/// 保证桌间距（0.8 同桌视觉压缩）与过道宽度的呈现一致。
/// </summary>
public class GridVisualGeometryTests
{
    [Fact]
    public void Grid几何_同桌压缩后步进与桌间一致()
    {
        var meta = new GridLayoutMetadata
        {
            Rows = 1,
            Columns = 4,
            SeatsPerDesk = 2,
            IntraDeskSpacing = 40,
            InterDeskSpacing = 32,
            VerticalSpacing = 56,
        };

        var geometry = GridVisualGeometryBuilder.Build(meta);
        var seats = geometry.Seats.Where(s => !s.IsDisabled).OrderBy(s => s.Column).ToList();
        seats.Should().HaveCount(4);

        var stepIntra = seats[1].X - seats[0].X;
        var stepInter = seats[2].X - seats[1].X;
        stepIntra.Should().BeApproximately(stepInter, 0.001, "同桌间距 40×0.8=32 与桌间距一致，视觉步进应相同");
        geometry.FactorX.Should().BeApproximately((PreviewSeatSize.SeatWidth / 0.9) / 32.0, 0.001);
    }

    [Fact]
    public void Grid几何_过道宽度参与步进()
    {
        var meta = new GridLayoutMetadata
        {
            Rows = 1,
            Columns = 3,
            SeatsPerDesk = 1,
            InterDeskSpacing = 32,
            AisleAfterColumns = [1],
            AisleWidth = 60,
            VerticalSpacing = 56,
        };

        var geometry = GridVisualGeometryBuilder.Build(meta);
        var seats = geometry.Seats.OrderBy(s => s.Column).ToList();
        var stepAisle = seats[1].X - seats[0].X;
        stepAisle.Should().BeApproximately(60 * geometry.FactorX, 0.001, "过道列按过道宽度参与步进");
    }

    [Fact]
    public void Grid几何_禁用座位以独立项返回()
    {
        var meta = new GridLayoutMetadata
        {
            Rows = 1,
            Columns = 2,
            SeatsPerDesk = 1,
            InterDeskSpacing = 32,
            VerticalSpacing = 56,
            EmptyPositions = [new GridPosition { Row = 1, Column = 2 }],
        };

        var geometry = GridVisualGeometryBuilder.Build(meta);

        geometry.Seats.Count(s => s.IsDisabled).Should().Be(1);
        geometry.Seats.Single(s => s.IsDisabled).SeatId.Should().Be("disabled-r1c2");
    }
}
