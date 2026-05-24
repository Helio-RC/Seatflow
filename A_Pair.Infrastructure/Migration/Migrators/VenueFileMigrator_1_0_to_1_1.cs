using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Infrastructure.Migration.Migrators;

/// <summary>
/// Venue 文件 1.0 → 1.1 迁移：
/// 将 Grid 布局座位的 JSON 数组从列主序重排为行主序（按 Row → Column 排序）。
/// </summary>
public sealed class VenueFileMigrator_1_0_to_1_1 : IFileMigrator
{
    private readonly ILogger<VenueFileMigrator_1_0_to_1_1> _logger;

    public VenueFileMigrator_1_0_to_1_1 (ILogger<VenueFileMigrator_1_0_to_1_1>? logger = null)
    {
        _logger = logger ?? NullLogger<VenueFileMigrator_1_0_to_1_1>.Instance;
    }

    public string FileType => "venue";
    public string FromVersion => "1.0";
    public string ToVersion => "1.1";

    public JsonNode Migrate (JsonNode root)
    {
        var layoutType = root["layout"]?["layoutType"]?.GetValue<string>();
        if (layoutType != "Grid" && layoutType != "Grid")
        {
            // 非 Grid 布局无需重排，仅更新版本号
            root["version"] = "1.1";
            return root;
        }

        var seats = root["layout"]?["seats"]?.AsArray();
        if (seats is null || seats.Count <= 1)
        {
            root["version"] = "1.1";
            return root;
        }

        // 将 seats 数组转为列表，按 Row → Column 排序
        var sorted = seats
            .OfType<JsonObject>()
            .Select(s => new
            {
                Node = s ,
                Row = s["row"]?.GetValue<int>() ?? int.MaxValue ,
                Column = s["column"]?.GetValue<int>() ?? int.MaxValue
            })
            .OrderBy(s => s.Row)
            .ThenBy(s => s.Column)
            .Select(s => s.Node)
            .ToList();

        seats.Clear();
        foreach (var s in sorted)
            seats.Add(s);

        root["version"] = "1.1";
        _logger.LogInformation("Venue 座位已重排为行主序，版本 1.0 → 1.1");
        return root;
    }
}
