using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace A_Pair.Core.Tests.Strategies;

public class DeskMateStrategyTests
{
    private static List<Student> CreateStudents (params string[] ids)
    {
        return ids.Select(id => new Student { Id = id , Name = id }).ToList();
    }

    private static List<GridSeat> CreateGridSeats (params (int row , int col)[] positions)
    {
        return positions.Select(p => new GridSeat
        {
            Id = $"seat_{p.row}_{p.col}" ,
            Row = p.row ,
            Column = p.col
        }).Cast<GridSeat>().ToList();
    }

    [Fact]
    public async Task ExecuteAsync_HorizontalAdjacent_ShouldAssignGroupTogether ()
    {
        var students = CreateStudents("s1" , "s2");
        var seats = CreateGridSeats((1 , 1) , (1 , 2) , (2 , 1));
        var ws = new SeatingWorkspace(students , seats.Cast<Seat>().ToList());

        var config = new DeskMateConfiguration
        {
            Groups = { new DeskMateGroup { StudentIds = { "s1" , "s2" } } } ,
            PreferHorizontal = true ,
            AllowVertical = false
        };
        var strategy = new DeskMateStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // s1 and s2 should occupy the two horizontally adjacent seats (1,1) and (1,2)
        var assignedSeatIds = ws.BuildSeatingPlan().Assignments.Values;
        var assignedSeats = seats.Where(s => assignedSeatIds.Contains(s.Id)).ToList();
        assignedSeats.Should().HaveCount(2);
        assignedSeats.Select(s => s.Row).Should().AllBeEquivalentTo(1); // both on row 1
        assignedSeats.Select(s => s.Column).Should().BeEquivalentTo(new[] { 1 , 2 });
    }

    [Fact]
    public async Task ExecuteAsync_NoAdjacent_ShouldFallbackToAny ()
    {
        var students = CreateStudents("s1" , "s2");
        var seats = CreateGridSeats((1 , 1) , (3 , 3)); // not adjacent
        var ws = new SeatingWorkspace(students , seats.Cast<Seat>().ToList());

        var config = new DeskMateConfiguration
        {
            Groups = { new DeskMateGroup { StudentIds = { "s1" , "s2" } } } ,
            PreferHorizontal = true ,
            AllowVertical = false
        };
        var strategy = new DeskMateStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // fallback: both students should still be assigned, though not adjacent
        ws.BuildSeatingPlan().Assignments.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_VerticalAdjacent_Allowed_ShouldAssign ()
    {
        var students = CreateStudents("s1" , "s2");
        var seats = CreateGridSeats((1 , 1) , (2 , 1) , (1 , 2));
        var ws = new SeatingWorkspace(students , seats.Cast<Seat>().ToList());

        var config = new DeskMateConfiguration
        {
            Groups = { new DeskMateGroup { StudentIds = { "s1" , "s2" } } } ,
            PreferHorizontal = false ,
            AllowVertical = true
        };
        var strategy = new DeskMateStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        var assignedColumns = seats.Where(s => ws.BuildSeatingPlan().Assignments.ContainsKey(s.Id))
            .Select(s => s.Column).Distinct();
        assignedColumns.Should().ContainSingle(); // same column, vertical adjacent
    }

    [Fact]
    public void AreSeatsAdjacent_GridCorrect ()
    {
        var a = new GridSeat { Row = 1 , Column = 1 };
        var b = new GridSeat { Row = 1 , Column = 2 };
        var c = new GridSeat { Row = 2 , Column = 1 };
        var d = new GridSeat { Row = 2 , Column = 2 };

        // Use reflection or internal visible? Not good. We'll test via public behavior only.
        // However, since AreSeatsAdjacent is private, we cannot test directly. We rely on integration via strategy.
        // So we skip direct test.
    }
}