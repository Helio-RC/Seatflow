using System.Text.Json.Nodes;
using SeatFlow.Core;
using SeatFlow.Infrastructure.Migration;

namespace SeatFlow.Infrastructure.Tests.Migration;

public class AppSettingsJsonMigrationTests
{
    /// <summary>磁盘真实形态：PascalCase + 旧遥测地址。</summary>
    private const string LegacyPascalCaseJson = """
    {
      "Version": "1.1",
      "Telemetry": {
        "Enabled": true,
        "ServerUrl": "https://seatflow.work/api/app/telemetry"
      }
    }
    """;

    [Fact]
    public void MigrateToCurrent_PascalCaseLegacyFile_ShouldRewriteTelemetryUrl()
    {
        // 遥测管线的早期配置读取（TelemetryServiceCollectionExtensions）走的就是这条，
        // 漏了它整条迁移对实际上是报链路无效。
        var json = AppSettingsJsonMigration.MigrateToCurrent(LegacyPascalCaseJson);

        var node = JsonNode.Parse(json)!;
        node["Version"]!.GetValue<string>().Should().Be(AppSettingsJsonMigration.CurrentVersion);
        node["Telemetry"]!["ServerUrl"]!.GetValue<string>().Should().Be(SeatFlowEndpoints.TelemetryUrl);
    }

    [Fact]
    public void MigrateToCurrent_VersionlessFile_ShouldRunWholeChain()
    {
        var json = AppSettingsJsonMigration.MigrateToCurrent("""
        {
          "Telemetry": { "ServerUrl": "https://seatflow.work/api/app/telemetry" }
        }
        """);

        JsonNode.Parse(json)!["Telemetry"]!["ServerUrl"]!.GetValue<string>()
            .Should().Be(SeatFlowEndpoints.TelemetryUrl);
    }

    [Fact]
    public void MigrateToCurrent_CustomUrl_ShouldBePreserved()
    {
        var json = AppSettingsJsonMigration.MigrateToCurrent("""
        { "Version": "1.1", "Telemetry": { "ServerUrl": "https://example.com/collect" } }
        """);

        JsonNode.Parse(json)!["Telemetry"]!["ServerUrl"]!.GetValue<string>()
            .Should().Be("https://example.com/collect");
    }

    [Fact]
    public void MigrateToCurrent_AlreadyCurrent_ShouldBeNoOp()
    {
        var json = AppSettingsJsonMigration.MigrateToCurrent("""
        { "Version": "1.2", "Telemetry": { "ServerUrl": "https://example.com/collect" } }
        """);

        JsonNode.Parse(json)!["Telemetry"]!["ServerUrl"]!.GetValue<string>()
            .Should().Be("https://example.com/collect");
    }
}
