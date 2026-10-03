namespace SeatFlow.Core.Tests.Strategies;

public class HeightPriorityStrategyTests
{
    private static Student MakeStudent(string id, float? height)
        => new() { Id = id, Name = id, Height = height };

    [Fact]
    public async Task ExecuteAsync_ShortestStudentGetsFrontSeat()
    {
        var students = new[] { MakeStudent("tall", 180), MakeStudent("short", 150), MakeStudent("mid", 165) };
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (2, 1), (3, 1));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);

        var strategy = new HeightPriorityStrategy();
        await strategy.ExecuteAsync(ws, CancellationToken.None);

        seats[0].OccupantId.Should().Be("short");
        seats[1].OccupantId.Should().BeNull();
        seats[2].OccupantId.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_MultipleFrontRows_FillsShortestFirst()
    {
        var students = new[] { MakeStudent("h170", 170), MakeStudent("h150", 150), MakeStudent("h160", 160) };
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (2, 1), (3, 1));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);

        var config = new HeightPriorityStrategy.HeightPriorityConfiguration { FrontRowCount = 2 };
        var strategy = new HeightPriorityStrategy(config);
        await strategy.ExecuteAsync(ws, CancellationToken.None);

        seats[0].OccupantId.Should().Be("h150");
        seats[1].OccupantId.Should().Be("h160");
        seats[2].OccupantId.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_StudentsWithoutHeightAreSkipped()
    {
        var students = new[] { MakeStudent("nosize", null), MakeStudent("short", 150) };
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (2, 1));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);

        var strategy = new HeightPriorityStrategy();
        await strategy.ExecuteAsync(ws, CancellationToken.None);

        seats[0].OccupantId.Should().Be("short");
        seats[1].OccupantId.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_NoHeightData_LogsInfoAndAssignsNothing()
    {
        var students = new[] { MakeStudent("a", null), MakeStudent("b", null) };
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (2, 1));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);

        var strategy = new HeightPriorityStrategy();
        await strategy.ExecuteAsync(ws, CancellationToken.None);

        ws.Messages.Should().Contain(m => m.MessageKey == "HeightPriority_NoHeightData");
        ws.BuildSeatingPlan().Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_NoFrontSeats_LogsWarning()
    {
        var students = new[] { MakeStudent("short", 150) };
        var seats = new Seat[] { new FreeformSeat { Id = "ff1", X = 1, Y = 1 } };
        var ws = new SeatingWorkspace(students, seats);

        var strategy = new HeightPriorityStrategy();
        await strategy.ExecuteAsync(ws, CancellationToken.None);

        ws.Messages.Should().Contain(m => m.MessageKey == "HeightPriority_NoSeats");
        ws.BuildSeatingPlan().Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotMoveAlreadyAssignedStudents()
    {
        var students = new[] { MakeStudent("short", 150), MakeStudent("tall", 180) };
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (2, 1));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_2_1", "tall", out _);

        var strategy = new HeightPriorityStrategy();
        await strategy.ExecuteAsync(ws, CancellationToken.None);

        seats[0].OccupantId.Should().Be("short");
        seats[1].OccupantId.Should().Be("tall");
    }

    [Fact]
    public async Task ExecuteAsync_Polar_FrontIsInnermostRing()
    {
        var students = new[] { MakeStudent("tall", 180), MakeStudent("short", 150) };
        var seats = new Seat[]
        {
            new PolarSeat { Id = "inner", Ring = 1, Radius = 1, AngleDegrees = 0 },
            new PolarSeat { Id = "outer", Ring = 3, Radius = 3, AngleDegrees = 0 },
        };
        var ws = new SeatingWorkspace(students, seats);

        var strategy = new HeightPriorityStrategy();
        await strategy.ExecuteAsync(ws, CancellationToken.None);

        seats[0].OccupantId.Should().Be("short");
        seats[1].OccupantId.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_NullWorkspace_ShouldThrowArgumentNullException()
    {
        var strategy = new HeightPriorityStrategy();
        var act = async () => await strategy.ExecuteAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void ValidateConfiguration_FrontRowCountZero_ShouldFail()
    {
        var config = new HeightPriorityStrategy.HeightPriorityConfiguration { FrontRowCount = 0 };
        var strategy = new HeightPriorityStrategy(config);
        strategy.ValidateConfiguration().IsValid.Should().BeFalse();
    }
}
