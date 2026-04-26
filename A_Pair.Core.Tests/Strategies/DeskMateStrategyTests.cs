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
        }).ToList();
    }

    [Fact]
    public async Task ExecuteAsync_HorizontalAdjacent_ShouldAssignGroupTogether ()
    {
        var students = CreateStudents("s1" , "s2");
        var seats = CreateGridSeats((1 , 1) , (1 , 2) , (2 , 1));
        var ws = new SeatingWorkspace(students , seats.Cast<Seat>().ToList());

        var config = new DeskMateConfiguration
        {
            Groups = new List<DeskMateGroup>
            {
                new DeskMateGroup { StudentIds = new List<string> { "s1", "s2" } }
            } ,
            PreferHorizontal = true ,
            AllowVertical = false
        };
        var strategy = new DeskMateStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        var plan = ws.BuildSeatingPlan();
        // 修正：使用 Keys（座位ID）查找被分配的座位
        var assignedSeats = seats.Where(s => plan.Assignments.ContainsKey(s.Id)).ToList();
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
            Groups = new List<DeskMateGroup>
            {
                new DeskMateGroup { StudentIds = new List<string> { "s1", "s2" } }
            } ,
            PreferHorizontal = true ,
            AllowVertical = false
        };
        var strategy = new DeskMateStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        var plan = ws.BuildSeatingPlan();
        plan.Assignments.Should().HaveCount(2); // both students assigned
    }

    [Fact]
    public async Task ExecuteAsync_VerticalAdjacent_Allowed_ShouldAssign ()
    {
        var students = CreateStudents("s1" , "s2");
        var seats = CreateGridSeats((1 , 1) , (2 , 1) , (1 , 2));
        var ws = new SeatingWorkspace(students , seats.Cast<Seat>().ToList());

        var config = new DeskMateConfiguration
        {
            Groups = new List<DeskMateGroup>
            {
                new DeskMateGroup { StudentIds = new List<string> { "s1", "s2" } }
            } ,
            PreferHorizontal = false ,
            AllowVertical = true
        };
        var strategy = new DeskMateStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        var plan = ws.BuildSeatingPlan();
        var assignedSeats = seats.Where(s => plan.Assignments.ContainsKey(s.Id)).ToList();
        assignedSeats.Should().HaveCount(2);
        // 垂直相邻意味着同列
        assignedSeats.Select(s => s.Column).Distinct().Should().ContainSingle();
    }
}