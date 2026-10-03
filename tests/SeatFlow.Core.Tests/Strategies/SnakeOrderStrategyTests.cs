namespace SeatFlow.Core.Tests.Strategies;

public class SnakeOrderStrategyTests
{
    private static GridSeat SeatAt(IEnumerable<GridSeat> seats, int row, int col)
        => seats.First(s => s.Row == row && s.Column == col);

    [Fact]
    public async Task ExecuteAsync_GridSerpentine_AlternatesRowDirection()
    {
        var students = StrategyTestHelpers.CreateStudents("s1", "s2", "s3", "s4", "s5", "s6");
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2), (1, 3), (2, 1), (2, 2), (2, 3));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);

        var strategy = new SnakeOrderStrategy();
        await strategy.ExecuteAsync(ws, CancellationToken.None);

        SeatAt(seats, 1, 1).OccupantId.Should().Be("s1");
        SeatAt(seats, 1, 2).OccupantId.Should().Be("s2");
        SeatAt(seats, 1, 3).OccupantId.Should().Be("s3");
        SeatAt(seats, 2, 3).OccupantId.Should().Be("s4");
        SeatAt(seats, 2, 2).OccupantId.Should().Be("s5");
        SeatAt(seats, 2, 1).OccupantId.Should().Be("s6");
    }

    [Fact]
    public async Task ExecuteAsync_SerpentineDisabled_FillsRowsSameDirection()
    {
        var students = StrategyTestHelpers.CreateStudents("s1", "s2", "s3", "s4");
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2), (2, 1), (2, 2));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);

        var config = new SnakeOrderStrategy.SnakeOrderConfiguration { Serpentine = false };
        var strategy = new SnakeOrderStrategy(config);
        await strategy.ExecuteAsync(ws, CancellationToken.None);

        SeatAt(seats, 1, 1).OccupantId.Should().Be("s1");
        SeatAt(seats, 1, 2).OccupantId.Should().Be("s2");
        SeatAt(seats, 2, 1).OccupantId.Should().Be("s3");
        SeatAt(seats, 2, 2).OccupantId.Should().Be("s4");
    }

    [Fact]
    public async Task ExecuteAsync_PolarSerpentine_AlternatesRingDirection()
    {
        var students = StrategyTestHelpers.CreateStudents("s1", "s2", "s3", "s4");
        var seats = new Seat[]
        {
            new PolarSeat { Id = "r1_a0", Ring = 1, Radius = 1, AngleDegrees = 0 },
            new PolarSeat { Id = "r1_a90", Ring = 1, Radius = 1, AngleDegrees = 90 },
            new PolarSeat { Id = "r2_a0", Ring = 2, Radius = 2, AngleDegrees = 0 },
            new PolarSeat { Id = "r2_a90", Ring = 2, Radius = 2, AngleDegrees = 90 },
        };
        var ws = new SeatingWorkspace(students, seats);

        var strategy = new SnakeOrderStrategy();
        await strategy.ExecuteAsync(ws, CancellationToken.None);

        seats.First(s => s.Id == "r1_a0").OccupantId.Should().Be("s1");
        seats.First(s => s.Id == "r1_a90").OccupantId.Should().Be("s2");
        // 第 2 环反向：先 90° 再 0°
        seats.First(s => s.Id == "r2_a90").OccupantId.Should().Be("s3");
        seats.First(s => s.Id == "r2_a0").OccupantId.Should().Be("s4");
    }

    [Fact]
    public async Task ExecuteAsync_SkipsAlreadyAssignedStudentsAndSeats()
    {
        var students = StrategyTestHelpers.CreateStudents("s1", "s2", "s3");
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2), (1, 3));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "s1", out _);

        var strategy = new SnakeOrderStrategy();
        await strategy.ExecuteAsync(ws, CancellationToken.None);

        SeatAt(seats, 1, 1).OccupantId.Should().Be("s1");
        SeatAt(seats, 1, 2).OccupantId.Should().Be("s2");
        SeatAt(seats, 1, 3).OccupantId.Should().Be("s3");
    }

    [Fact]
    public async Task ExecuteAsync_NoSeats_SkipsWithoutError()
    {
        var students = StrategyTestHelpers.CreateStudents("s1");
        var seats = new Seat[] { new GridSeat { Id = "g1", Row = 1, Column = 1, IsFixed = true } };
        var ws = new SeatingWorkspace(students, seats);

        var strategy = new SnakeOrderStrategy();
        var result = await strategy.ExecuteAsync(ws, CancellationToken.None);

        result.Success.Should().BeTrue();
        ws.BuildSeatingPlan().Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_NullWorkspace_ShouldThrowArgumentNullException()
    {
        var strategy = new SnakeOrderStrategy();
        var act = async () => await strategy.ExecuteAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void ValidateConfiguration_AlwaysValid()
    {
        var strategy = new SnakeOrderStrategy();
        strategy.ValidateConfiguration().IsValid.Should().BeTrue();
    }
}
