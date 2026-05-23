namespace A_Pair.Core.Tests.Strategies;

public class RandomFillStrategyTests
{
    private static List<Student> CreateStudents (int count)
    {
        return Enumerable.Range(1 , count).Select(i => new Student { Id = $"s{i}" , Name = $"Student{i}" }).ToList();
    }

    private static List<Seat> CreateGridSeats (int rows , int cols)
    {
        var seats = new List<Seat>();
        for (int r = 1; r <= rows; r++)
            for (int c = 1; c <= cols; c++)
                seats.Add(new GridSeat { Id = $"seat_{r}_{c}" , Row = r , Column = c });
        return seats;
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFillAllEmptySeats_WhenEnoughStudents ()
    {
        var students = CreateStudents(5);
        var seats = CreateGridSeats(2 , 3); // 6 seats
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new RandomFillStrategy();
        var result = await strategy.ExecuteAsync(ws , CancellationToken.None);

        result.Success.Should().BeTrue();
        ws.GetEmptySeats().Count().Should().Be(1); // 5 filled, 1 empty
        ws.BuildSeatingPlan().Assignments.Should().HaveCount(5);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotAssignMoreThanStudents ()
    {
        var students = CreateStudents(2);
        var seats = CreateGridSeats(3 , 3);
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new RandomFillStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        ws.BuildSeatingPlan().Assignments.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ShouldThrow ()
    {
        var students = CreateStudents(100);
        var seats = CreateGridSeats(10 , 10);
        var ws = new SeatingWorkspace(students , seats);

        var cts = new CancellationTokenSource();
        cts.Cancel();
        var strategy = new RandomFillStrategy();
        await strategy.Awaiting(s => s.ExecuteAsync(ws , cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ValidateConfiguration_AlwaysValid ()
    {
        var strategy = new RandomFillStrategy();
        var result = strategy.ValidateConfiguration();
        result.IsValid.Should().BeTrue();
    }
}