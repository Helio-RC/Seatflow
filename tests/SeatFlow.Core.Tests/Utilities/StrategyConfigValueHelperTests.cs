using System.Text.Json;

namespace SeatFlow.Core.Tests.Utilities;

public class StrategyConfigValueHelperTests
{
    [Fact]
    public void ParseStudentIds_JsonArray_ShouldPreserveOrderAndDedupe()
    {
        using var doc = JsonDocument.Parse("""["s1", "s2", "s1", "", "s3"]""");

        var ids = StrategyConfigValueHelper.ParseStudentIds(doc.RootElement);

        ids.Should().Equal("s1", "s2", "s3");
    }

    [Fact]
    public void ParseStudentIds_Enumerable_ShouldIgnoreNullsAndDuplicates()
    {
        var ids = StrategyConfigValueHelper.ParseStudentIds(new List<object?> { "s1", null, "s2", "s1" });

        ids.Should().Equal("s1", "s2");
    }

    [Fact]
    public void ParseStudentIds_CommaSeparatedString_ShouldTrimAndDedupe()
    {
        var ids = StrategyConfigValueHelper.ParseStudentIds("a, b ,a");

        ids.Should().Equal("a", "b");
    }

    [Fact]
    public void ParseStudentIds_JsonStringCsv_ShouldParse()
    {
        using var doc = JsonDocument.Parse("\"a,b\"");

        var ids = StrategyConfigValueHelper.ParseStudentIds(doc.RootElement);

        ids.Should().Equal("a", "b");
    }

    [Fact]
    public void ParseStudentIds_NullOrUnsupported_ShouldReturnEmpty()
    {
        StrategyConfigValueHelper.ParseStudentIds(null).Should().BeEmpty();
        StrategyConfigValueHelper.ParseStudentIds(42).Should().BeEmpty();
    }
}
