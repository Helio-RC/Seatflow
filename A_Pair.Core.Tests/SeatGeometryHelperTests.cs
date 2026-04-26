namespace A_Pair.Core.Tests;

public class SeatGeometryHelperTests
{
    [Fact]
    public void GetPosition_GridSeat_ShouldCalculateCorrectly ()
    {
        var seat = new GridSeat { Row = 2 , Column = 3 };
        var meta = new GridLayoutMetadata { HorizontalSpacing = 1.0 , VerticalSpacing = 1.5 , OriginX = 0 , OriginY = 0 };
        var (x , y) = SeatGeometryHelper.GetPosition(seat , meta);
        x.Should().Be(2.0); // (3-1)*1.0 = 2
        y.Should().Be(1.5); // (2-1)*1.5 = 1.5
    }

    [Fact]
    public void GetPosition_PolarSeat_ShouldConvertToCartesian ()
    {
        var seat = new PolarSeat { Radius = 2.0 , AngleDegrees = 90 };
        var meta = new PolarLayoutMetadata { OriginX = 0 , OriginY = 0 };
        var (x , y) = SeatGeometryHelper.GetPosition(seat , meta);
        x.Should().BeApproximately(0 , 1e-9);
        y.Should().BeApproximately(2.0 , 1e-9);
    }

    [Fact]
    public void GetPosition_FreeformSeat_ShouldReturnExact ()
    {
        var seat = new FreeformSeat { X = 3.3 , Y = 4.4 };
        var (x , y) = SeatGeometryHelper.GetPosition(seat , new LayoutMetadata());
        x.Should().Be(3.3);
        y.Should().Be(4.4);
    }
}