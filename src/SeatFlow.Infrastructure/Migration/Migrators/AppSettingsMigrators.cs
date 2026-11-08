using System.Text.Json.Nodes;
using SeatFlow.Core;

namespace SeatFlow.Infrastructure.Migration.Migrators;

/// <summary>
/// AppSettings.json 各版本迁移器。<br/>
/// 注意：磁盘上的 <c>AppSettings.json</c> 用 PascalCase 序列化（<c>JsonOptions.WriteIndented</c> 未设命名策略），
/// 而仓储层按 <c>version</c> 字段判断版本，故键名查找统一走 <see cref="JsonKeyCase"/>。
/// </summary>
public static class AppSettingsMigrators
{
    /// <summary>
    /// 1.0 → 1.1：无结构变更，仅补版本戳，使更早的文件也能串到后续步骤。
    /// </summary>
    public sealed class Step_1_0_to_1_1 : IFileMigrator
    {
        public string FileType => "appSettings";
        public string FromVersion => "1.0";
        public string ToVersion => "1.1";

        public JsonNode Migrate(JsonNode root)
        {
            if (root is JsonObject rootObject)
                SetVersion(rootObject, "1.1");

            return root;
        }
    }

    /// <summary>
    /// 1.1 → 1.2：把「仍是旧默认值」的遥测地址改写为新后端域名。<br/>
    /// 只认字面量默认值——<see cref="SeatFlow.Core.Models.TelemetryConfig.ServerUrl"/> 是用户可编辑字段，
    /// 静默改写用户显式配置比漏改更糟。
    /// </summary>
    public sealed class Step_1_1_to_1_2 : IFileMigrator
    {
        /// <summary>1.1 及更早版本的遥测默认地址。</summary>
        internal const string LegacyTelemetryUrl = "https://seatflow.work/api/app/telemetry";

        public string FileType => "appSettings";
        public string FromVersion => "1.1";
        public string ToVersion => "1.2";

        public JsonNode Migrate(JsonNode root)
        {
            if (root is not JsonObject rootObject)
                return root;

            var telemetryKey = JsonKeyCase.FindKey(rootObject, "telemetry");
            if (telemetryKey is not null && rootObject[telemetryKey] is JsonObject telemetry)
            {
                if (JsonKeyCase.FindKey(telemetry, "serverUrl") is { } key
                    && telemetry[key] is JsonValue value
                    && value.TryGetValue<string>(out var url)
                    && IsLegacyDefault(url))
                {
                    telemetry[key] = SeatFlowEndpoints.TelemetryUrl;
                }
            }

            SetVersion(rootObject, "1.2");
            return rootObject;
        }

        /// <summary>忽略首尾空白、尾随斜杠与大小写差异后，判断是否等于旧默认地址。</summary>
        internal static bool IsLegacyDefault(string? url)
            => Normalize(url) == Normalize(LegacyTelemetryUrl);

        private static string Normalize(string? url)
            => (url ?? string.Empty).Trim().TrimEnd('/').ToUpperInvariant();
    }

    /// <summary>改写既有版本键（保留原来的大小写）；不存在时按 camelCase 新建。</summary>
    private static void SetVersion(JsonObject root, string version)
    {
        root[JsonKeyCase.FindKey(root, "version") ?? "version"] = version;
    }
}
