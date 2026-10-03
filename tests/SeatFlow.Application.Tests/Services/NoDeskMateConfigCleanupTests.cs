using SeatFlow.Core.Utilities;

namespace SeatFlow.Application.Tests.Services;

/// <summary>NoDeskMate 配置清理：过滤已删除学生、丢弃不足两人的行、消费端去重。</summary>
public class NoDeskMateConfigCleanupTests
{
    [Fact]
    public void Clean_FiltersDeletedMembers_AndDropsShortRows()
    {
        var config = new StrategyDatasetConfig
        {
            Rows =
            [
                new() { Index = 1, Values = new() { ["members"] = new List<string> { "a", "b", "x" } } },
                new() { Index = 2, Values = new() { ["members"] = new List<string> { "a", "x" } } },
                new() { Index = 3, Values = new() { ["members"] = new List<string> { "a", "b" } } },
            ]
        };
        var valid = new HashSet<string> { "a", "b" };

        bool changed = ApplicationFacade.CleanNoDeskMateDeletedStudents(config, valid);

        changed.Should().BeTrue();
        config.Rows.Should().HaveCount(2);
        StrategyConfigValueHelper.ParseStudentIds(config.Rows[0].Values["members"]).Should().Equal("a", "b");
        config.Rows[1].Index.Should().Be(3);
    }

    [Fact]
    public void Clean_NoChanges_ReturnsFalse()
    {
        var config = new StrategyDatasetConfig
        {
            Rows = [new() { Values = new() { ["members"] = new List<string> { "a", "b" } } }]
        };

        bool changed = ApplicationFacade.CleanNoDeskMateDeletedStudents(config, new HashSet<string> { "a", "b" });

        changed.Should().BeFalse();
        config.Rows.Should().HaveCount(1);
    }

    [Fact]
    public void Clean_DuplicateMembers_DedupedAtParse()
    {
        var config = new StrategyDatasetConfig
        {
            Rows = [new() { Values = new() { ["members"] = new List<string> { "a", "a", "b" } } }]
        };

        ApplicationFacade.CleanNoDeskMateDeletedStudents(config, new HashSet<string> { "a", "b" });

        StrategyConfigValueHelper.ParseStudentIds(config.Rows[0].Values["members"]).Should().Equal("a", "b");
    }
}
