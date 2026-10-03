using SeatFlow.Core.Enums;

namespace SeatFlow.Core.Tests.Strategies;

public class GenderDeskMateStrategyTests
{
    private static List<Student> Students(params (string id, Gender? gender)[] items)
        => [.. items.Select(i => new Student { Id = i.id, Name = i.id, Gender = i.gender })];

    [Fact]
    public async Task EvaluateAsync_MixedMode_SameGenderDeskMate_ShouldReject()
    {
        var students = Students(("m1", Gender.Male), ("m2", Gender.Male));
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "m1", out _);

        var strategy = new GenderDeskMateStrategy();
        var result = await strategy.EvaluateAsync(ws, students[1], seats[1], StrategyTestHelpers.CreateContext(), CancellationToken.None);

        result.Approved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_MixedMode_OppositeGenderDeskMate_ShouldApprove()
    {
        var students = Students(("m1", Gender.Male), ("f1", Gender.Female));
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "m1", out _);

        var strategy = new GenderDeskMateStrategy();
        var result = await strategy.EvaluateAsync(ws, students[1], seats[1], StrategyTestHelpers.CreateContext(), CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_SameGenderMode_OppositeGenderDeskMate_ShouldReject()
    {
        var students = Students(("m1", Gender.Male), ("f1", Gender.Female));
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "m1", out _);

        var config = new GenderDeskMateStrategy.GenderDeskMateConfiguration { PreferMixed = false };
        var strategy = new GenderDeskMateStrategy(config);
        var result = await strategy.EvaluateAsync(ws, students[1], seats[1], StrategyTestHelpers.CreateContext(), CancellationToken.None);

        result.Approved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_SameGenderMode_SameGenderDeskMate_ShouldApprove()
    {
        var students = Students(("m1", Gender.Male), ("m2", Gender.Male));
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "m1", out _);

        var config = new GenderDeskMateStrategy.GenderDeskMateConfiguration { PreferMixed = false };
        var strategy = new GenderDeskMateStrategy(config);
        var result = await strategy.EvaluateAsync(ws, students[1], seats[1], StrategyTestHelpers.CreateContext(), CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_UnknownGender_ShouldApprove()
    {
        var students = Students(("m1", Gender.Male), ("x1", null));
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "m1", out _);

        var strategy = new GenderDeskMateStrategy();
        var result = await strategy.EvaluateAsync(ws, students[1], seats[1], StrategyTestHelpers.CreateContext(), CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_RerollExhausted_ShouldApproveWithWarning()
    {
        var students = Students(("m1", Gender.Male), ("m2", Gender.Male));
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "m1", out _);

        var context = new StrategyTestHelpers.TestContext(rerollCount: 9, maxRerolls: 10);
        var strategy = new GenderDeskMateStrategy();
        var result = await strategy.EvaluateAsync(ws, students[1], seats[1], context, CancellationToken.None);

        result.Approved.Should().BeTrue();
        context.Warnings.Should().ContainSingle(w => w.MessageKey == "GenderDeskMate_Forced");
    }

    [Fact]
    public async Task EvaluateAsync_FixedSeat_ShouldApprove()
    {
        var students = Students(("m1", Gender.Male), ("m2", Gender.Male));
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (1, 2));
        seats[1].IsFixed = true;
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "m1", out _);

        var strategy = new GenderDeskMateStrategy();
        var result = await strategy.EvaluateAsync(ws, students[1], seats[1], StrategyTestHelpers.CreateContext(), CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_NotDeskMates_ShouldApprove()
    {
        var students = Students(("m1", Gender.Male), ("m2", Gender.Male));
        var seats = StrategyTestHelpers.CreateGridSeats((1, 1), (2, 1));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_1", "m1", out _);

        var strategy = new GenderDeskMateStrategy();
        var result = await strategy.EvaluateAsync(ws, students[1], seats[1], StrategyTestHelpers.CreateContext(), CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_DifferentDeskWithinRow_ShouldApprove()
    {
        // seatsPerDesk=2：列 1/2 为一桌，列 3 属于下一桌
        var students = Students(("m1", Gender.Male), ("m2", Gender.Male));
        var seats = StrategyTestHelpers.CreateGridSeats((1, 2), (1, 3));
        var ws = new SeatingWorkspace(students, [.. seats.Cast<Seat>()]);
        ws.TryAssignSeat("seat_1_2", "m1", out _);

        var strategy = new GenderDeskMateStrategy();
        strategy.SetSeatsPerDesk(2);
        var result = await strategy.EvaluateAsync(ws, students[1], seats[1], StrategyTestHelpers.CreateContext(), CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_NullWorkspace_ShouldThrowArgumentNullException()
    {
        var strategy = new GenderDeskMateStrategy();
        var act = async () => await strategy.EvaluateAsync(
            null!, new Student(), new GridSeat(), StrategyTestHelpers.CreateContext(), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
