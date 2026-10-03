using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 多选学生选择器。用于一行配置中选择多个学生（如「不为同桌」的搭配组）。
/// 选中结果按学生 ID 集合维护，天然去重；支持排除列表（跨行防重复）。
/// </summary>
public partial class MultiStudentPickerViewModel : ViewModelBase
{
    public MultiStudentPickerViewModel(IDialogService? dialog = null) : base(dialog ?? NullDialogService.Instance) { }

    private List<StudentPickerItem> _allStudents = [];
    private HashSet<string> _excludedStudentIds = [];
    private bool _suppressSelectionChanged;

    [ObservableProperty]
    public partial ObservableCollection<SelectableStudentItem> Students { get; set; } = [];

    /// <summary>选中项变化通知（供编辑器做跨行防重复）。</summary>
    public event Action? SelectionChanged;

    /// <summary>当前选中的学生 ID（按列表顺序，无重复）。</summary>
    public IReadOnlyList<string> SelectedIds => [.. Students.Where(s => s.IsSelected).Select(s => s.Id)];

    /// <summary>当前选中人数。</summary>
    public int SelectedCount => Students.Count(s => s.IsSelected);

    /// <summary>摘要文案：未选显示占位提示，已选显示人数。</summary>
    public string SummaryText => SelectedCount == 0
        ? Resources.StudentPicker_MultiPlaceholder
        : string.Format(Resources.StudentPicker_MultiSelectedFmt, SelectedCount);

    /// <summary>从学生列表加载可选项（按姓名自然序），保留已选状态。</summary>
    public void LoadStudents(IEnumerable<SeatFlow.Core.Models.Student> students)
    {
        var selected = new HashSet<string>(SelectedIds, StringComparer.Ordinal);
        _allStudents = [.. Helpers.StudentSorter.Sort(students)
            .Select(s => new StudentPickerItem { Id = s.Id, Name = s.Name })];
        Rebuild(selected);
    }

    /// <summary>按 ID 集合设置选中项（自动忽略不存在的 ID、自动去重）。</summary>
    public void SetSelectedIds(IEnumerable<string> ids) => Rebuild([.. ids]);

    /// <summary>
    /// 设置排除的学生 ID 集合。排除项不出现在列表中（已选中项除外，避免选中状态丢失）。
    /// </summary>
    public void SetExcludedIds(HashSet<string> excludedIds)
    {
        _excludedStudentIds = excludedIds ?? [];
        Rebuild(new HashSet<string>(SelectedIds, StringComparer.Ordinal));
    }

    /// <summary>清空所有选中项。</summary>
    [RelayCommand]
    private void Clear()
    {
        _suppressSelectionChanged = true;
        foreach (var item in Students)
            item.IsSelected = false;
        _suppressSelectionChanged = false;
        NotifySelectionState();
        SelectionChanged?.Invoke();
    }

    private void Rebuild(HashSet<string> selected)
    {
        foreach (var item in Students)
            item.PropertyChanged -= OnItemPropertyChanged;

        _suppressSelectionChanged = true;
        Students = new ObservableCollection<SelectableStudentItem>(
            _allStudents
                .Where(s => !_excludedStudentIds.Contains(s.Id) || selected.Contains(s.Id))
                .Select(s => new SelectableStudentItem
                {
                    Id = s.Id,
                    Name = s.Name,
                    IsSelected = selected.Contains(s.Id)
                }));
        foreach (var item in Students)
            item.PropertyChanged += OnItemPropertyChanged;
        _suppressSelectionChanged = false;

        NotifySelectionState();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectableStudentItem.IsSelected))
            return;

        NotifySelectionState();
        if (!_suppressSelectionChanged)
            SelectionChanged?.Invoke();
    }

    private void NotifySelectionState()
    {
        OnPropertyChanged(nameof(SelectedIds));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SummaryText));
    }
}

/// <summary>可勾选的学生项（多选选择器用）。</summary>
public partial class SelectableStudentItem : ObservableObject
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Display => string.IsNullOrEmpty(Name) ? Id : Name;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
