using System.Collections.ObjectModel;
using System.Windows.Input;
using SeatFlow.Core.Models;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 学生名单表格编辑契约：人员管理页与 OOBE 名单步骤共用 <c>StudentRosterTable</c>。
/// 两页各自决定排序/写回语义，控件只负责表头、行内编辑与新增行。
/// </summary>
public interface IStudentRosterEditor
{
    ObservableCollection<StudentRowViewModel> Students { get; }

    Student NewStudent { get; set; }

    bool UseStaticRoster { get; }

    bool UseVirtualization { get; }

    ICommand BeginEditCommand { get; }

    ICommand EndEditCommand { get; }

    ICommand CancelEditCommand { get; }

    ICommand DeleteStudentCommand { get; }

    ICommand AddNewStudentCommand { get; }
}
