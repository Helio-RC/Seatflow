using System.Text.Json.Nodes;

namespace SeatFlow.Infrastructure.Migration;

/// <summary>
/// JSON 键名大小写兼容查找。<br/>
/// 持久化文件的键名并不统一：多数文件用 camelCase，而 <c>AppSettings.json</c> 是 PascalCase
/// （<c>JsonOptions.WriteIndented</c> 未设命名策略）。凡是「磁盘上可能是任一大小写」的键，
/// 读写都统一走这里，避免各处重复实现。
/// </summary>
internal static class JsonKeyCase
{
    /// <summary>
    /// 按 camelCase / PascalCase 两种写法查找键名，返回**命中的实际键名**；都不存在则返回 <c>null</c>。
    /// 返回键名而非节点，是因为写回时还要用同一个键，不能凭空造出第二种大小写。
    /// </summary>
    public static string? FindKey(JsonObject parent, string camelCaseKey)
    {
        if (parent.ContainsKey(camelCaseKey)) return camelCaseKey;

        var pascalCaseKey = char.ToUpperInvariant(camelCaseKey[0]) + camelCaseKey[1..];
        return parent.ContainsKey(pascalCaseKey) ? pascalCaseKey : null;
    }

    /// <summary>按两种大小写取节点值；父节点为空或键不存在时返回 <c>null</c>。</summary>
    public static JsonNode? Find(JsonObject? parent, string camelCaseKey)
        => parent is not null && FindKey(parent, camelCaseKey) is { } key ? parent[key] : null;
}
