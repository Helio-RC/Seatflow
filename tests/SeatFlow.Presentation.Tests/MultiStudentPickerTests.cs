using FluentAssertions;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>多选学生选择器与配置行序列化（「不为同桌」搭配组）。</summary>
public class MultiStudentPickerTests
{
    private static List<Student> Students(params string[] ids)
        => [.. ids.Select(id => new Student { Id = id, Name = id })];

    [Fact]
    public void 选中_更新人数与摘要()
    {
        var vm = new MultiStudentPickerViewModel();
        vm.LoadStudents(Students("s1", "s2", "s3"));

        vm.Students[0].IsSelected = true;
        vm.Students[1].IsSelected = true;

        vm.SelectedCount.Should().Be(2);
        vm.SelectedIds.Should().Equal("s1", "s2");
        vm.SummaryText.Should().Contain("2");
    }

    [Fact]
    public void 设置选中_自动去重并忽略未知ID()
    {
        var vm = new MultiStudentPickerViewModel();
        vm.LoadStudents(Students("s1", "s2"));

        vm.SetSelectedIds(["s1", "s1", "missing"]);

        vm.SelectedIds.Should().Equal("s1");
    }

    [Fact]
    public void 排除项不显示_但保留已选()
    {
        var vm = new MultiStudentPickerViewModel();
        vm.LoadStudents(Students("s1", "s2", "s3"));
        vm.SetSelectedIds(["s2"]);

        vm.SetExcludedIds(["s1", "s2"]);

        vm.Students.Select(s => s.Id).Should().Equal("s2", "s3");
        vm.SelectedIds.Should().Equal("s2");
    }

    [Fact]
    public void 清空_重置选择()
    {
        var vm = new MultiStudentPickerViewModel();
        vm.LoadStudents(Students("s1", "s2"));
        vm.SetSelectedIds(["s1", "s2"]);

        vm.ClearCommand.Execute(null);

        vm.SelectedCount.Should().Be(0);
        vm.SelectedIds.Should().BeEmpty();
    }

    [Fact]
    public void 多选配置行_序列化往返_保留成员()
    {
        var codeBlock = new StrategyCodeBlock
        {
            DataType = StrategyDataType.Student,
            ShowSeatPosition = false,
            StudentPickerMultiSelect = true
        };
        var row = new StrategyConfigRow
        {
            Index = 1,
            Values = new Dictionary<string, object?> { ["members"] = new List<string> { "s2", "s1", "s2" } }
        };
        var vm = ConfigBlockRowViewModel.FromConfigRow(row, codeBlock, 1);

        vm.LoadStudents(Students("s1", "s2", "s3"));

        vm.IsMultiSelect.Should().BeTrue();
        vm.MultiPicker!.SelectedIds.Should().Equal("s1", "s2");

        var serialized = vm.ToConfigRow();
        serialized.StudentId.Should().BeNull();
        serialized.Values["members"].Should().BeEquivalentTo(new List<string> { "s1", "s2" });
    }
}
