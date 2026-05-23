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
    public void BuildGrid_SeatsShouldHaveCorrectPositions ()
    {
        var layout = GridLayoutBuilder.BuildGrid(2 , 2);
        var seats = layout.Seats.Cast<GridSeat>().ToList();
        // 列优先：第1列行1,行2; 第2列行1,行2
        seats[0].Row.Should().Be(1);
        seats[0].Column.Should().Be(1);
        seats[1].Row.Should().Be(2);
        seats[1].Column.Should().Be(1);
        seats[2].Row.Should().Be(1);
        seats[2].Column.Should().Be(2);
        seats[3].Row.Should().Be(2);
        seats[3].Column.Should().Be(2);
    }

    [Fact]
    public void BuildGrid_WithColumnRowCounts_ShouldUsePerColumnRows ()
    {
        var meta = new GridLayoutMetadata
        {
            Rows = 10 ,
            Columns = 3 ,
            SeatsPerDesk = 1 ,
            ColumnRowCounts = new List<int> { 3 , 2 , 1 }
        };
        var layout = GridLayoutBuilder.BuildGrid(meta);
        layout.Seats.Count.Should().Be(6); // 3+2+1
        var seats = layout.Seats.Cast<GridSeat>().ToList();
        seats.Count(s => s.Column == 1).Should().Be(3);
        seats.Count(s => s.Column == 2).Should().Be(2);
        seats.Count(s => s.Column == 3).Should().Be(1);
    }

    [Fact]
    public void BuildGrid_WithEmptyPositions_ShouldSkipThem ()
    {
        var meta = new GridLayoutMetadata
        {
            Rows = 3 ,
            Columns = 3 ,
            SeatsPerDesk = 1 ,
            EmptyPositions = new List<GridPosition>
            {
                new() { Row = 1, Column = 2 },
                new() { Row = 2, Column = 3 }
            }
        };
        var layout = GridLayoutBuilder.BuildGrid(meta);
        layout.Seats.Count.Should().Be(7); // 9 - 2
        layout.Seats.Cast<GridSeat>().Any(s => s.Row == 1 && s.Column == 2).Should().BeFalse();
        layout.Seats.Cast<GridSeat>().Any(s => s.Row == 2 && s.Column == 3).Should().BeFalse();
    }

    [Fact]
    public void BuildGrid_WithColumnRowCounts_BackwardCompat_EmptyListUsesRows ()
    {
        var meta = new GridLayoutMetadata
        {
            Rows = 4 ,
            Columns = 3 ,
            SeatsPerDesk = 1 ,
            ColumnRowCounts = [] // 空列表 -> 回退到 Rows
        };
        var layout = GridLayoutBuilder.BuildGrid(meta);
        layout.Seats.Count.Should().Be(12); // 4*3
    }
}