using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Controls;

/// <summary>
/// 可复用的学生名单表格：表头 + 虚拟化列表 + 底部新增行。
/// 排序与写回语义由 <see cref="Editor"/> 所属页面决定，控件只负责行内编辑交互。
/// </summary>
public partial class StudentRosterTable : UserControl
{
    public static readonly StyledProperty<IStudentRosterEditor?> EditorProperty =
        AvaloniaProperty.Register<StudentRosterTable, IStudentRosterEditor?>(nameof(Editor));

    public static readonly StyledProperty<double> ListMaxHeightProperty =
        AvaloniaProperty.Register<StudentRosterTable, double>(nameof(ListMaxHeight), double.PositiveInfinity);

    public StudentRosterTable()
    {
        InitializeComponent();
    }

    /// <summary>名单编辑器（人员管理页或 OOBE 名单步骤的 ViewModel）。</summary>
    public IStudentRosterEditor? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <summary>列表最大高度；默认不限制，由宿主布局决定（OOBE 设为固定值以便卡片自适应）。</summary>
    public double ListMaxHeight
    {
        get => GetValue(ListMaxHeightProperty);
        set => SetValue(ListMaxHeightProperty, value);
    }

    /// <summary>点击行（显示态）进入行内编辑；编辑控件上的点击不触发。</summary>
    private void OnStudentRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is TextBox or ComboBox or CheckBox or Button) return;
        if (sender is Border { DataContext: StudentRowViewModel row } && !row.IsEditing)
            Editor?.BeginEditCommand.Execute(row);
    }

    /// <summary>
    /// 行内编辑键盘：Enter 提交并退出编辑态；Esc 回滚并退出。
    /// Tab 依赖 Avalonia 默认焦点遍历（姓名 → 身高 → 性别 → 前排 → 操作按钮）。
    /// </summary>
    private void OnRowEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: StudentRowViewModel row } || Editor is null)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                Editor.EndEditCommand.Execute(row);
                e.Handled = true;
                break;
            case Key.Escape:
                Editor.CancelEditCommand.Execute(row);
                e.Handled = true;
                break;
        }
    }

    /// <summary>新增行 Name 文本框按 Enter 时提交。</summary>
    private void OnNewStudentNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Editor is null) return;

        Editor.AddNewStudentCommand.Execute(null);
        e.Handled = true;
    }
}
