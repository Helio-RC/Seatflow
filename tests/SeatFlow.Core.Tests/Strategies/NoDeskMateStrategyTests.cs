namespace SeatFlow.Core.Tests.Strategies;

public class NoDeskMateStrategyTests
{
    private static void SetGroups(NoDeskMateStrategy strategy, params string[][] groups)
        => strategy.SetGroups(groups);

    [Fact]
    public async Task EvaluateAsync_SameGroupDeskMate_ShouldReject()
    {
        var students = StrategyTestHelpers.CreateStudents("a", "b");
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "a", out _);

        var strategy = new NoDeskMateStrategy();
        SetGroups(strategy, ["a", "b"]);
        var result = await strategy.EvaluateAsync(ws, students[1], seats[1], StrategyTestHelpers.CreateContext(), CancellationToken.None);

        result.Approved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_DifferentGroupDeskMate_ShouldApprove()
    {
        var students = StrategyTestHelpers.CreateStudents("a", "c");
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "a", out _);

        var strategy = new NoDeskMateStrategy();
        SetGroups(strategy, ["a", "b"], ["c", "d"]);
        var result = await strategy.EvaluateAsync(ws, students[1], seats[1], StrategyTestHelpers.CreateContext(), CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_CrossGroupMember_ShouldRespectUnion()
    {
        var students = StrategyTestHelpers.CreateStudents("a", "b");
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "b", out _);

        // a 同时属于两个组，任一组合命中即拒绝
        var strategy = new NoDeskMateStrategy();
        SetGroups(strategy, ["a", "b"], ["a", "c"]);
        var result = await strategy.EvaluateAsync(ws, students[0], seats[1], StrategyTestHelpers.CreateContext(), CancellationToken.None);

        result.Approved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_NotDeskMates_ShouldApprove()
    {
        var students = StrategyTestHelpers.CreateStudents("a", "b");
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (2, 1));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "a", out _);

        var strategy = new NoDeskMateStrategy();
        SetGroups(strategy, ["a", "b"]);
        var result = await strategy.EvaluateAsync(ws, students[1], seats[1], StrategyTestHelpers.CreateContext(), CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_GroupWithSingleMember_ShouldBeIgnored()
    {
        var students = StrategyTestHelpers.CreateStudents("a", "b");
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "a", out _);

        var strategy = new NoDeskMateStrategy();
        SetGroups(strategy, ["a"]);
        var result = await strategy.EvaluateAsync(ws, students[1], seats[1], StrategyTestHelpers.CreateContext(), CancellationToken.None);

        strategy.Config.Groups.Should().BeEmpty();
        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_RerollExhausted_ShouldApproveWithWarning()
    {
        var students = StrategyTestHelpers.CreateStudents("a", "b");
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "a", out _);

        var context = new StrategyTestHelpers.TestContext(rerollCount: 9, maxRerolls: 10);
        var strategy = new NoDeskMateStrategy();
        SetGroups(strategy, ["a", "b"]);
        var result = await strategy.EvaluateAsync(ws, students[1], seats[1], context, CancellationToken.None);

        result.Approved.Should().BeTrue();
        context.Warnings.Should().ContainSingle(w => w.MessageKey == "NoDeskMate_Forced");
    }

    [Fact]
    public async Task EvaluateAsync_FixedSeat_ShouldApprove()
    {
        var students = StrategyTestHelpers.CreateStudents("a", "b");
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2));
        seats[1].IsFixed = true;
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "a", out _);

        var strategy = new NoDeskMateStrategy();
        SetGroups(strategy, ["a", "b"]);
        var result = await strategy.EvaluateAsync(ws, students[1], seats[1], StrategyTestHelpers.CreateContext(), CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    [Fact]
    public void SetGroups_DuplicateMembers_ShouldDedupeInGroup()
    {
        var strategy = new NoDeskMateStrategy();

        SetGroups(strategy, ["a", "a", "b"]);

        strategy.Config.Groups.Should().HaveCount(1);
        strategy.Config.Groups[0].Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public async Task EvaluateAsync_NullWorkspace_ShouldThrowArgumentNullException()
    {
        var strategy = new NoDeskMateStrategy();
        var act = async () => await strategy.EvaluateAsync(
            null!, new Student(), new GridSeat(), StrategyTestHelpers.CreateContext(), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
