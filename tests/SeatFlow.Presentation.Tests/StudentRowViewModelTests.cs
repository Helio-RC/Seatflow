using FluentAssertions;
using SeatFlow.Core.Enums;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 名单行「显示 / 编辑」包装 <see cref="StudentRowViewModel"/> 的键盘语义测试（M6）：
/// Esc 取消（回滚到进入编辑态前的快照）与 Enter 提交（丢弃快照，保留改动）。
/// </summary>
public class StudentRowViewModelTests
{
    private static StudentRowViewModel CreateRow() => new(new Student
    {
        Name = "Alice",
        Height = 165,
        Gender = Gender.Female,
        NeedsFrontRow = false,
    });

    [Fact]
    public void CancelEdit_回滚全部代理字段并退出编辑态()
    {
        var row = CreateRow();
        row.IsEditing = true;
        row.SnapshotForEdit();

        row.NameText = "Bob";
        row.Height = 180;
        row.GenderIndex = 1;
        row.IsFrontRow = true;

        row.CancelEdit();

        row.IsEditing.Should().BeFalse();
        row.Student.Name.Should().Be("Alice");
        row.Student.Height.Should().Be(165);
        row.Student.Gender.Should().Be(Gender.Female);
        row.Student.NeedsFrontRow.Should().BeFalse();
        row.NameText.Should().Be("Alice");
        row.HeightDisplay.Should().Be("165");
        row.GenderIndex.Should().Be(2);
    }

    [Fact]
    public void 提交后_改动保留_再取消不回滚()
    {
        var row = CreateRow();
        row.IsEditing = true;
        row.SnapshotForEdit();

        row.NameText = "Bob";
        row.Height = 180;

        // Enter 提交语义：丢弃编辑快照并退出编辑态
        row.ClearEditSnapshot();
        row.IsEditing = false;

        row.Student.Name.Should().Be("Bob");
        row.Student.Height.Should().Be(180);

        // 提交后即使再调用 CancelEdit 也不应回滚（快照已清）
        row.CancelEdit();
        row.Student.Name.Should().Be("Bob");
        row.Student.Height.Should().Be(180);
    }

    [Fact]
    public void 编辑代理字段变更触发显示态通知()
    {
        var row = CreateRow();
        var changes = new List<string?>();
        row.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        row.NameText = "Carol";
        row.Height = 170;
        row.GenderIndex = 3;
        row.IsFrontRow = true;

        changes.Should().Contain(nameof(StudentRowViewModel.Name));
        changes.Should().Contain(nameof(StudentRowViewModel.HeightDisplay));
        changes.Should().Contain(nameof(StudentRowViewModel.GenderDisplay));
        changes.Should().Contain(nameof(StudentRowViewModel.NeedsFrontRow));
    }
}
