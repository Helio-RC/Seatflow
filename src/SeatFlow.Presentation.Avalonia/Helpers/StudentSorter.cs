using System;
using System.Collections.Generic;
using System.Linq;

namespace SeatFlow.Presentation.Avalonia.Helpers;

/// <summary>
/// 学生列表显示排序：姓名自然序（汉字按拼音、数字按大小），同名回退 Id 保证稳定。
/// 落盘顺序仍由仓储决定，二者互不影响。
/// </summary>
public static class StudentSorter
{
    public static IEnumerable<SeatFlow.Core.Models.Student> Sort(
        IEnumerable<SeatFlow.Core.Models.Student> students)
        => students
            .OrderBy(s => s.Name, NaturalStringComparer.Instance)
            .ThenBy(s => s.Id, StringComparer.Ordinal);
}
