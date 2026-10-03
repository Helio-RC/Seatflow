using FluentAssertions;
using SeatFlow.Presentation.Avalonia.Helpers;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 姓名自然排序比较器：数字按数值、文本按中文区域（汉字拼音、忽略大小写）。
/// </summary>
public class NaturalStringComparerTests
{
    private static List<string> Sort(IEnumerable<string> values)
        => [.. values.OrderBy(v => v, NaturalStringComparer.Instance)];

    [Fact]
    public void 数字段应按数值大小排序()
        => Sort(["学生10", "学生2", "学生1"]).Should().Equal("学生1", "学生2", "学生10");

    [Fact]
    public void 纯数字应忽略前导零并按数值排序()
        => Sort(["10", "02", "2", "1"]).Should().Equal("1", "2", "02", "10");

    [Fact]
    public void 应忽略大小写且保持稳定()
        => Sort(["bob", "Alice", "alice"]).Should().Equal("Alice", "alice", "bob");

    [Fact]
    public void 汉字应按拼音排序()
        => Sort(["张三", "李四", "王五"]).Should().Equal("李四", "王五", "张三");

    [Fact]
    public void 空字符串应排在最前()
        => Sort(["b", "", "a"]).Should().Equal("", "a", "b");
}
