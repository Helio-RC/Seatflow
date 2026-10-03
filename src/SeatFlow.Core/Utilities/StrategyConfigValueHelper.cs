using System.Text.Json;

namespace SeatFlow.Core.Utilities;

/// <summary>
/// 策略数据集配置值的解析辅助，供 Application 运行时与 Presentation 配置编辑器共用。
/// </summary>
public static class StrategyConfigValueHelper
{
    /// <summary>
    /// 解析学生 ID 列表值（多选学生选择器的 <c>Values["members"]</c>）。
    /// 支持 JSON 数组（磁盘反序列化后为 <see cref="JsonElement"/>）、任意可枚举集合与逗号分隔字符串；
    /// 保留顺序、去除重复项、忽略空项。
    /// </summary>
    public static List<string> ParseStudentIds(object? value)
    {
        IEnumerable<string?> raw = value switch
        {
            null => [],
            JsonElement je => ParseJsonElement(je),
            string s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            System.Collections.IEnumerable e => e.Cast<object?>().Select(o => o?.ToString()),
            _ => []
        };

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in raw)
        {
            if (string.IsNullOrEmpty(id))
                continue;
            if (seen.Add(id))
                result.Add(id);
        }
        return result;
    }

    private static IEnumerable<string?> ParseJsonElement(JsonElement je)
    {
        if (je.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in je.EnumerateArray())
                yield return item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
        }
        else if (je.ValueKind == JsonValueKind.String)
        {
            foreach (var part in je.GetString()!.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return part;
            }
        }
    }
}
