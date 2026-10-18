using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using FluentAssertions;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>共享名单表格：点击行进入编辑，Enter 提交；人员管理页与 OOBE 共用同一交互。</summary>
public class StudentRosterTableTests
{
    [AvaloniaFact]
    public void 点击行进入编辑_Enter提交()
    {
        var row = new StudentRowViewModel(new Student { Name = "张三" });
        var editor = new FakeEditor(row);
        var table = new StudentRosterTable { Editor = editor };
        var window = new Window { Width = 600, Height = 400, Content = table };
        window.Show();
        window.UpdateLayout();

        var rowBorder = table.GetVisualDescendants()
            .OfType<Border>()
            .First(b => ReferenceEquals(b.DataContext, row));
        var point = rowBorder.TranslatePoint(new Point(20, 18), window)!.Value;

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.UpdateLayout();

        row.IsEditing.Should().BeTrue("点击显示态行应进入行内编辑");

        var nameBox = rowBorder.GetVisualDescendants().OfType<TextBox>().First(t => t.IsVisible);
        nameBox.Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.UpdateLayout();

        row.IsEditing.Should().BeFalse("Enter 应提交并退出编辑态");
        window.Close();
    }

    private sealed class FakeEditor : IStudentRosterEditor
    {
        public FakeEditor(StudentRowViewModel row)
        {
            Students = [row];
            BeginEditCommand = new RelayCommand<StudentRowViewModel?>(target =>
            {
                if (target is null) return;
                foreach (var item in Students)
                    item.IsEditing = ReferenceEquals(item, target);
            });
            EndEditCommand = new RelayCommand<StudentRowViewModel?>(target =>
            {
                if (target is not null) target.IsEditing = false;
            });
            CancelEditCommand = new RelayCommand<StudentRowViewModel?>(target => target?.CancelEdit());
            DeleteStudentCommand = new RelayCommand<StudentRowViewModel?>(target =>
            {
                if (target is not null) Students.Remove(target);
            });
            AddNewStudentCommand = new RelayCommand(() => { });
        }

        public ObservableCollection<StudentRowViewModel> Students { get; }

        public Student NewStudent { get; set; } = new();

        public bool UseStaticRoster => true;

        public bool UseVirtualization => false;

        public ICommand BeginEditCommand { get; }

        public ICommand EndEditCommand { get; }

        public ICommand CancelEditCommand { get; }

        public ICommand DeleteStudentCommand { get; }

        public ICommand AddNewStudentCommand { get; }
    }
}
