using FluentAssertions;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 人员管理表格显示排序：姓名自然序 + Id 兜底（此前直接展示仓储按 GUID 排序的顺序）。
/// </summary>
public class MemberStudentSortingTests
{
    [Fact]
    public void 应按姓名自然排序_同名按Id兜底()
    {
        var students = new List<Student>
        {
            new() { Id = "z", Name = "张三" },
            new() { Id = "b", Name = "学生10" },
            new() { Id = "a", Name = "学生2" },
            new() { Id = "c", Name = "李四" },
            new() { Id = "d", Name = "李四" },
        };

        var sorted = MemberManagementViewModel.SortStudents(students)
            .Select(s => (s.Name, s.Id))
            .ToList();

        sorted.Should().Equal(
            ("李四", "c"),
            ("李四", "d"),
            ("学生2", "a"),
            ("学生10", "b"),
            ("张三", "z"));
    }
}
