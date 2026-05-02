using System;
using System.Globalization;
using A_Pair.Core.Enums;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace A_Pair.Presentation.Avalonia.Converters;

/// <summary>
/// 安全转换 Gender? ↔ string，无效输入返回 null。
/// </summary>
public class GenderConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Gender g)
        {
            return g switch
            {
                Gender.Male => "男",
                Gender.Female => "女",
                Gender.Other => "其他",
                _ => g.ToString()
            };
        }
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value switch
        {
            global::Avalonia.Controls.ComboBoxItem item => item.Content?.ToString(),
            string str => str,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(s))
            return null;

        s = s.Trim();
        return s switch
        {
            "Male" or "male" or "男" or "男性" => Gender.Male,
            "Female" or "female" or "女" or "女性" => Gender.Female,
            "Other" or "other" or "其他" => Gender.Other,
            _ => null
        };
    }
}

/// <summary>
/// 安全转换 bool ↔ string（是/否），无效输入返回 false。
/// </summary>
public class NeedsFrontRowConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "是" : "否";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        s = s.Trim();
        return s switch
        {
            "true" or "True" or "TRUE" or "是" or "Y" or "y" or "1" => true,
            _ => false
        };
    }
}

/// <summary>
/// 安全转换 float? ↔ string，无效输入返回 null。
/// </summary>
public class HeightConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() ?? "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s))
            return null;

        return float.TryParse(s, out var h) ? h : null;
    }
}
