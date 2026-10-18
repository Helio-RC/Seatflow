using FluentAssertions;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// Grid 会场预览构建：座位/禁用位/讲台/门 overlay 与会场页几何保持一致，
/// 并统一走 24px 归一化。
/// </summary>
public class VenueGridPreviewBuilderTests
{
    private static GridLayoutMetadata CreateMetadata() => new()
    {
        Rows = 2,
        Columns = 4,
        SeatsPerDesk = 2,
        HorizontalSpacing = 40,
        VerticalSpacing = 36,
        OriginX = 200,
        OriginY = 200,
        IntraDeskSpacing = 12,
        InterDeskSpacing = 32,
        AisleWidth = 60,
        EmptyPositions = [new GridPosition { Row = 1, Column = 2 }],
    };

    [Fact]
    public void Build_生成座位与禁用位_并保留选中态()
    {
        var meta = CreateMetadata();
        var selected = new HashSet<(int Row, int Column)> { (1, 2) };

        var snapshot = VenueGridPreviewBuilder.Build(meta, selected);

        snapshot.Seats.Should().HaveCount(meta.Rows * meta.Columns);
        var disabled = snapshot.Seats.Single(s => s.IsDisabled);
        disabled.Row.Should().Be(1);
        disabled.Column.Should().Be(2);
        disabled.IsSelected.Should().BeTrue();
        disabled.SeatLabel.Should().Contain("1");
        snapshot.Seats.Count(s => s.IsSelected).Should().Be(1);
    }

    [Fact]
    public void Build_讲台与门overlay_使用同一几何系数()
    {
        var meta = CreateMetadata();
        var geometry = GridVisualGeometryBuilder.Build(meta);
        var door = new BoardOverlay(10, 20, 36, 24, "门", IsDoor: true);

        var snapshot = VenueGridPreviewBuilder.Build(meta, null, [door]);

        snapshot.Overlays.Should().NotBeNull();
        var overlays = snapshot.Overlays!;
        overlays.Should().Contain(o => o.IsDoor);
        overlays.Should().Contain(o => !o.IsDoor && o.Label != null, "讲台 overlay 应存在");

        var doorOverlay = overlays.Single(o => o.IsDoor);
        doorOverlay.Width.Should().Be(36);
        doorOverlay.Height.Should().Be(24);

        // 归一化后门与座位共享同一平移量；用座位坐标反推缩放系数，验证 X/Y 使用了几何系数。
        var rawSeat = geometry.Seats.First();
        var movedSeat = snapshot.Seats.First(s => s.Row == rawSeat.Row && s.Column == rawSeat.Column);
        double dx = movedSeat.X - rawSeat.X;
        double dy = movedSeat.Y - rawSeat.Y;
        doorOverlay.X.Should().BeApproximately((10 * geometry.FactorX) + dx, 0.001);
        doorOverlay.Y.Should().BeApproximately((20 * geometry.FactorY) + dy, 0.001);
    }

    [Fact]
    public void Build_归一化后最小坐标为24px()
    {
        var snapshot = VenueGridPreviewBuilder.Build(CreateMetadata());

        var minX = snapshot.Seats.Min(s => s.X);
        var minY = snapshot.Seats.Min(s => s.Y);
        if (snapshot.Overlays is { Count: > 0 })
        {
            minX = Math.Min(minX, snapshot.Overlays.Min(o => o.X));
            minY = Math.Min(minY, snapshot.Overlays.Min(o => o.Y));
        }

        minX.Should().BeApproximately(24, 0.001);
        minY.Should().BeApproximately(24, 0.001);
        snapshot.BoardWidth.Should().BeGreaterThanOrEqualTo(320);
        snapshot.BoardHeight.Should().BeGreaterThanOrEqualTo(240);
    }

    [Fact]
    public void Build_无座位时返回空快照()
    {
        var meta = new GridLayoutMetadata { Rows = 0, Columns = 0, HasPodium = true };

        var snapshot = VenueGridPreviewBuilder.Build(meta);

        snapshot.Seats.Should().BeEmpty();
        snapshot.Overlays.Should().BeNullOrEmpty();
        snapshot.Should().BeSameAs(SeatLayoutSnapshot.Empty);
    }
}
