using SeatFlow.Core.Enums;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Lang;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 名单行「显示 / 编辑」轻量包装（M4）：<see cref="Student"/> 为 Core 模型，不引入 UI 状态；
/// 本类承载 <see cref="IsEditing"/> 与编辑代理属性，写回 Student 并触发 PropertyChanged，
/// 供脏检查统一比较（每行显示态只渲染 TextBlock，编辑态才创建输入控件）。
/// </summary>
public partial class StudentRowViewModel : ObservableObject
{
    public Student Student { get; }

    public StudentRowViewModel(Student student)
    {
        Student = student;
    }

    /// <summary>是否处于行内编辑态（同一时刻仅一行）。</summary>
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    // ── 显示态 ──

    public string Name => Student.Name;

    public string HeightDisplay => Student.Height is { } h ? h.ToString("0.#") : string.Empty;

    public string GenderDisplay => Student.Gender switch
    {
        Gender.Male => Resources.Gender_Male,
        Gender.Female => Resources.Gender_Female,
        Gender.Other => Resources.Gender_Other,
        _ => string.Empty
    };

    public bool NeedsFrontRow => Student.NeedsFrontRow;

    // ── 编辑态代理（写回 Student 并通知） ──

    public string NameText
    {
        get => Student.Name;
        set
        {
            if (Student.Name == value) return;
            Student.Name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Name));
        }
    }

    public float? Height
    {
        get => Student.Height;
        set
        {
            if (Student.Height == value) return;
            Student.Height = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HeightDisplay));
        }
    }

    /// <summary>0=未设置, 1=男, 2=女, 3=其他（与 ComboBox SelectedIndex 对齐）。</summary>
    public int GenderIndex
    {
        get => Student.Gender switch
        {
            Gender.Male => 1,
            Gender.Female => 2,
            Gender.Other => 3,
            _ => 0
        };
        set
        {
            var gender = value switch
            {
                1 => Gender.Male,
                2 => Gender.Female,
                3 => Gender.Other,
                _ => (Gender?)null
            };
            if (Student.Gender == gender) return;
            Student.Gender = gender;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GenderDisplay));
        }
    }

    public bool IsFrontRow
    {
        get => Student.NeedsFrontRow;
        set
        {
            if (Student.NeedsFrontRow == value) return;
            Student.NeedsFrontRow = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NeedsFrontRow));
        }
    }
}
