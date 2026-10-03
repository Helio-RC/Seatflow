using System;
using System.Collections.Generic;
using System.Globalization;

namespace SeatFlow.Core.Utilities;

/// <summary>
/// 人员姓名自然排序比较器：
/// 文本段按中文区域比较（汉字按拼音、忽略大小写/全半角），
/// 数字段按数值大小比较（如「学生2」排在「学生10」之前）；
/// 数值相等时按原始长度保证全序。null/空串排在最前。
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static NaturalStringComparer Instance { get; } = new();

    private const CompareOptions TextOptions =
        CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType;

    private static readonly CompareInfo ZhCompare = CultureInfo.GetCultureInfo("zh-CN").CompareInfo;

    private NaturalStringComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            bool xDigit = char.IsAsciiDigit(x[i]);
            bool yDigit = char.IsAsciiDigit(y[j]);

            if (xDigit && yDigit)
            {
                int numeric = CompareNumericRun(x, ref i, y, ref j);
                if (numeric != 0) return numeric;
            }
            else if (!xDigit && !yDigit)
            {
                int xStart = i, yStart = j;
                while (i < x.Length && !char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && !char.IsAsciiDigit(y[j])) j++;
                int text = ZhCompare.Compare(x, xStart, i - xStart, y, yStart, j - yStart, TextOptions);
                if (text != 0) return text;
            }
            else
            {
                // 数字段优先于文本段（如「1」排在「A」前）
                return xDigit ? -1 : 1;
            }
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }

    private static int CompareNumericRun(string x, ref int i, string y, ref int j)
    {
        int xStart = i, yStart = j;
        while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
        while (j < y.Length && char.IsAsciiDigit(y[j])) j++;

        var xSpan = x.AsSpan(xStart, i - xStart);
        var ySpan = y.AsSpan(yStart, j - yStart);

        // 跳过前导零后：先比有效位数，再比字符
        int xSig = 0, ySig = 0;
        while (xSig < xSpan.Length - 1 && xSpan[xSig] == '0') xSig++;
        while (ySig < ySpan.Length - 1 && ySpan[ySig] == '0') ySig++;

        int lengthCompare = (xSpan.Length - xSig).CompareTo(ySpan.Length - ySig);
        if (lengthCompare != 0) return lengthCompare;

        int charCompare = xSpan[xSig..].SequenceCompareTo(ySpan[ySig..]);
        if (charCompare != 0) return charCompare;

        // 数值相等（前导零数量不同），按原始长度保证全序
        return xSpan.Length.CompareTo(ySpan.Length);
    }
}
