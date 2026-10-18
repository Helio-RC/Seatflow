using System.Windows.Input;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>人员管理页实现共享名单表格契约（业务命令仍由现有行内编辑命令驱动）。</summary>
public partial class MemberManagementViewModel : IStudentRosterEditor
{
    ICommand IStudentRosterEditor.BeginEditCommand => BeginEditCommand;

    ICommand IStudentRosterEditor.EndEditCommand => EndEditCommand;

    ICommand IStudentRosterEditor.CancelEditCommand => CancelEditCommand;

    ICommand IStudentRosterEditor.DeleteStudentCommand => DeleteStudentCommand;

    ICommand IStudentRosterEditor.AddNewStudentCommand => AddNewStudentCommand;
}
