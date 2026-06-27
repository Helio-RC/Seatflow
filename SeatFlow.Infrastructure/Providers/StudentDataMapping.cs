using SeatFlow.Core.Enums;
using SeatFlow.Core.Models;

namespace SeatFlow.Infrastructure.Providers;

/// <summary>
/// 学生数据导入的列名映射和值转换工具。
/// 支持中英文列名，第二行作为注释行自动跳过。
/// </summary>
internal static class StudentDataMapping
{
    /// <summary>第 2 行为注释行，数据从第 3 行开始。</summary>
    public const int DataStartRow = 3;

    /// <summary>列名到 Student 属性名的映射表。</summary>
    private static readonly Dictionary<string , string> ColumnMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // English
        ["Name"] = "Name" ,
        ["Height"] = "Height" ,
        ["Gender"] = "Gender" ,
        ["NeedsFrontRow"] = "NeedsFrontRow" ,
        // 中文
        ["姓名"] = "Name" ,
        ["身高"] = "Height" ,
        ["性别"] = "Gender" ,
        ["需要前排"] = "NeedsFrontRow" ,
        ["前排"] = "NeedsFrontRow"
    };

    /// <summary>解析列名，返回对应的 Student 属性名。</summary>
    public static string? ResolveProperty (string columnName)
    {
        var trimmed = columnName.Trim();
        return ColumnMap.TryGetValue(trimmed , out var prop) ? prop : null;
    }

    /// <summary>
    /// 将单元格值设置到 Student 对象的对应属性上。
    /// </summary>
    public static void SetProperty (Student student , string propertyName , string? rawValue)
    {
        switch (propertyName)
        {
            case "Name":
                student.Name = rawValue?.Trim() ?? string.Empty;
                break;
            case "Height":
                if (float.TryParse(rawValue , out var h))
                    student.Height = h;
                break;
            case "Gender":
                student.Gender = ParseGender(rawValue?.Trim());
                break;
            case "NeedsFrontRow":
                student.NeedsFrontRow = ParseBool(rawValue?.Trim());
                break;
        }
    }

    private static Gender? ParseGender (string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value switch
        {
            "Male" or "male" or "男" or "男性" => Gender.Male,
            "Female" or "female" or "女" or "女性" => Gender.Female,
            "Other" or "other" or "其他" => Gender.Other,
            _ => null
        };
    }

    private static bool ParseBool (string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value switch
        {
            "true" or "True" or "TRUE" or "是" or "Y" or "y" or "1" => true,
            _ => false
        };
    }
}
