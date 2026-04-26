namespace A_Pair.Core.Tests.Strategies;

public class FixedSeatStrategyTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldApplyFixedAssignments ()
    {
        var config = new FixedSeatConfiguration
        {
            FixedAssignments = new Dictionary<string , string> { { "seat1" , "s1" } }
        };
        var students = new[] { new Student { Id = "s1" } };
        var seats = new[] { new GridSeat { Id = "seat1" } };
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new FixedSeatStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats[0].IsFixed.Should().BeTrue();
        seats[0].OccupantId.Should().Be("s1");
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyFixed_ShouldNotOverride ()
    {
        var config = new FixedSeatConfiguration();
        var students = new[] { new Student { Id = "s1" } };
        var seats = new[] { new GridSeat { Id = "seat1" , IsFixed = true , OccupantId = "s1" } };
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new FixedSeatStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats[0].IsFixed.Should().BeTrue();
        seats[0].OccupantId.Should().Be("s1"); // unchanged
    }
}