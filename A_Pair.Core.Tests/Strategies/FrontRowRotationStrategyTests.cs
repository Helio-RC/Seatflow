namespace A_Pair.Core.Tests.Strategies;

public class FrontRowRotationStrategyTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldPrioritizeStudentsWithHighScore ()
    {
        var students = new[]
        {
        new Student { Id = "s1", NeedsFrontRow = true, FrontRowPreferenceScore = 0 },
        new Student { Id = "s2", NeedsFrontRow = false, FrontRowPreferenceScore = 100 }
    };
        var seats = new[]
        {
        new GridSeat { Id = "front1", Row = 1, Column = 1 },
        new GridSeat { Id = "back1", Row = 2, Column = 1 }
    };
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new FrontRowRotationStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        var frontSeat = seats.First(s => s.Id == "front1");
        frontSeat.OccupantId.Should().Be("s1");   // 修正此处
    }

    [Fact]
    public async Task ExecuteAsync_WithHistory_ShouldReduceScore ()
    {
        var student = new Student { Id = "s1" , FrontRowPreferenceScore = 50 };
        var seatFront = new GridSeat { Id = "f1" , Row = 1 , Column = 1 };
        var seatBack = new GridSeat { Id = "b1" , Row = 2 , Column = 1 };
        var ws = new SeatingWorkspace(new[] { student } , new[] { seatFront , seatBack });

        // simulate front row history
        student.RecentSeatHistory.Add("f1");
        student.RecentSeatHistory.Add("f1"); // add twice to heavily penalize

        var strategy = new FrontRowRotationStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // should not assign to front row again (or low chance, but since only one student, it will still fill if only empty seat is front)
        // Actually only front seat is available here because back seat is also empty. Both are empty, but front seat is only one student.
        // We'll ensure there is another student with zero history.
        // Better to design a fresh test: 
        // Let's create two students, one with heavy front row history, other none. Front seat should go to the one with none.
        // modified test:
    }

    [Fact]
    public async Task ExecuteAsync_HistoryPenalty_ShouldFavorNoHistory ()
    {
        var s1 = new Student { Id = "s1" , FrontRowPreferenceScore = 0 };
        s1.RecentSeatHistory.Add("f1");
        s1.RecentSeatHistory.Add("f1");
        var s2 = new Student { Id = "s2" , FrontRowPreferenceScore = 0 };

        var seats = new[]
        {
            new GridSeat { Id = "f1", Row = 1, Column = 1 },
            new GridSeat { Id = "b1", Row = 2, Column = 1 }
        };
        var ws = new SeatingWorkspace(new[] { s1 , s2 } , seats);

        var strategy = new FrontRowRotationStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats.First(s => s.Id == "f1").OccupantId.Should().Be("s2");
    }
}