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

    [Fact]
    public void ValidateConfiguration_HasGroups_ShouldPass ()
    {
        var config = new DeskMateConfiguration
        {
            Groups = new List<DeskMateGroup>
            {
                new DeskMateGroup { StudentIds = new List<string> { "s1", "s2" } }
            }
        };
        var strategy = new DeskMateStrategy(config);
        var result = strategy.ValidateConfiguration();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateConfiguration_NullGroups_ShouldFail ()
    {
        var config = new DeskMateConfiguration { Groups = null! };
        var strategy = new DeskMateStrategy(config);
        var result = strategy.ValidateConfiguration();
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Constructor_NullConfig_ShouldThrowArgumentNullException ()
    {
        var act = () => new DeskMateStrategy(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_NullWorkspace_ShouldThrowArgumentNullException ()
    {
        var strategy = new DeskMateStrategy();
        var act = async () => await strategy.ExecuteAsync(null! , CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_GroupsFromAttributeBag_ShouldMergeGroups ()
    {
        var s1 = new Student { Id = "s1" };
        s1.Extensions.Set("DeskMates" , new List<string> { "s2" });
        var s2 = new Student { Id = "s2" };

        var seats = CreateGridSeats((1 , 1) , (1 , 2));
        var ws = new SeatingWorkspace(new[] { s1 , s2 } , seats.Cast<Seat>().ToList());

        var strategy = new DeskMateStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        var plan = ws.BuildSeatingPlan();
        plan.Assignments.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_GroupWithOnlyOneUnassigned_ShouldBePlacedNearAssignedMate ()
    {
        var s1 = new Student { Id = "s1" };
        var s2 = new Student { Id = "s2" };
        var s3 = new Student { Id = "s3" };

        var seats = CreateGridSeats((1 , 1) , (1 , 2) , (2 , 1));
        var ws = new SeatingWorkspace(new[] { s1 , s2 , s3 } , seats.Cast<Seat>().ToList());

        // Pre-assign s1 to (2,1) — the group {s1, s2} only has s2 unassigned
        ws.TryAssignSeat(seats[2].Id , s1.Id , out _);

        var config = new DeskMateConfiguration
        {
            Groups = new List<DeskMateGroup>
            {
                new DeskMateGroup { StudentIds = new List<string> { "s1", "s2" } }
            }
        };
        var strategy = new DeskMateStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // s2 should be placed adjacent to s1 (near-occupied)
        var plan = ws.BuildSeatingPlan();
        plan.Assignments.Should().HaveCount(2);
        // s1 stays at (2,1), s2 assigned to adjacent (1,1)
        plan.Assignments[seats[0].Id].Should().Be("s2");
    }

    [Fact]
    public async Task ExecuteAsync_PolarSeatsWithLogicalGroup_ShouldConsiderAdjacent ()
    {
        var students = CreateStudents("s1" , "s2");
        var seats = new List<Seat>
        {
            new PolarSeat { Id = "p1", Ring = 1, Radius = 1, AngleDegrees = 0, LogicalGroup = "A" },
            new PolarSeat { Id = "p2", Ring = 1, Radius = 1, AngleDegrees = 45, LogicalGroup = "A" },
            new PolarSeat { Id = "p3", Ring = 1, Radius = 1, AngleDegrees = 180, LogicalGroup = "B" },
        };
        var ws = new SeatingWorkspace(students , seats);

        var config = new DeskMateConfiguration
        {
            Groups = new List<DeskMateGroup>
            {
                new DeskMateGroup { StudentIds = new List<string> { "s1", "s2" } }
            }
        };
        var strategy = new DeskMateStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        var plan = ws.BuildSeatingPlan();
        plan.Assignments.Should().HaveCount(2);
        // p1 and p2 share LogicalGroup "A" → adjacent → assigned
        plan.Assignments.Keys.Should().Contain(new[] { "p1" , "p2" });
    }

    [Fact]
    public async Task ExecuteAsync_FreeformSeatsWithLogicalGroup_ShouldConsiderAdjacent ()
    {
        var students = CreateStudents("s1" , "s2");
        var seats = new List<Seat>
        {
            new FreeformSeat { Id = "ff1", X = 0, Y = 0, LogicalGroup = "G1" },
            new FreeformSeat { Id = "ff2", X = 10, Y = 10, LogicalGroup = "G1" },
            new FreeformSeat { Id = "ff3", X = 50, Y = 50, LogicalGroup = "G2" },
        };
        var ws = new SeatingWorkspace(students , seats);

        var config = new DeskMateConfiguration
        {
            Groups = new List<DeskMateGroup>
            {
                new DeskMateGroup { StudentIds = new List<string> { "s1", "s2" } }
            }
        };
        var strategy = new DeskMateStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        var plan = ws.BuildSeatingPlan();
        plan.Assignments.Should().HaveCount(2);
        // ff1 and ff2 share LogicalGroup "G1" → adjacent despite large distance
        plan.Assignments.Keys.Should().Contain(new[] { "ff1" , "ff2" });
    }

    [Fact]
    public async Task ExecuteAsync_BfsFallback_WhenNoContiguousBlock ()
    {
        var students = CreateStudents("s1" , "s2" , "s3");
        // Seats are all isolated (no grid adjacency, no LogicalGroup) — BFS finds only size-1 components
        var seats = new List<Seat>
        {
            new GridSeat { Id = "a1", Row = 1, Column = 1 },
            new GridSeat { Id = "a3", Row = 1, Column = 3 },
            new GridSeat { Id = "c1", Row = 3, Column = 1 },
        };
        var ws = new SeatingWorkspace(students , seats);

        var config = new DeskMateConfiguration
        {
            Groups = new List<DeskMateGroup>
            {
                new DeskMateGroup { StudentIds = new List<string> { "s1", "s2" } }
            },
            PreferHorizontal = true,
            AllowVertical = false,
        };
        var strategy = new DeskMateStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // BFS finds no component of size >= 2 → falls back to sequential: assigns first 2 seats
        var plan = ws.BuildSeatingPlan();
        plan.Assignments.Should().HaveCount(2);
    }

    [Fact]
    public void ValidateConfiguration_GroupWithLessThanTwoStudents_ShouldFail ()
    {
        var config = new DeskMateConfiguration
        {
            Groups = new List<DeskMateGroup>
            {
                new DeskMateGroup { StudentIds = new List<string> { "s1" } }
            }
        };
        var strategy = new DeskMateStrategy(config);
        var result = strategy.ValidateConfiguration();
        result.IsValid.Should().BeFalse();
    }
}