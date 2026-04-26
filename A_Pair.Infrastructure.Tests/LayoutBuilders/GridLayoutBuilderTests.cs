namespace A_Pair.Infrastructure.Tests.LayoutBuilders;

public class GridLayoutBuilderTests
{
    [Fact]
    public void BuildGrid_ShouldCreateCorrectNumberOfSeats ()
    {
        var layout = GridLayoutBuilder.BuildGrid(3 , 4);
        layout.Seats.Should().HaveCount(12);
        layout.LayoutType.Should().Be(LayoutType.Grid);
        var meta = layout.Metadata as GridLayoutMetadata;
        meta.Should().NotBeNull();
        meta!.Rows.Should().Be(3);
        meta.Columns.Should().Be(4);
    }

    [Fact]
    public void BuildGrid_SeatsShouldHaveConsecutiveIds ()
    {
        var layout = GridLayoutBuilder.BuildGrid(2 , 2);
        var seats = layout.Seats.Cast<GridSeat>().ToList();
        seats[0].Row.Should().Be(1);
        seats[0].Column.Should().Be(1);
        seats[3].Row.Should().Be(2);
        seats[3].Column.Should().Be(2);
    }
}