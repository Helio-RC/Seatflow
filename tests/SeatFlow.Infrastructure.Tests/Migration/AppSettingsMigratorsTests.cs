using System.Text.Json.Nodes;
using SeatFlow.Core;
using SeatFlow.Infrastructure.Migration;
using SeatFlow.Infrastructure.Migration.Migrators;

namespace SeatFlow.Infrastructure.Tests.Migration;

public class AppSettingsMigratorsTests
{
    /// <summary>磁盘上的真实形态：PascalCase（SaveAsync 用 JsonOptions.WriteIndented，未设命名策略）。</summary>
    private static JsonObject MakeSettings(string? serverUrl, string version = "1.1")
    {
        var telemetry = new JsonObject();
        if (serverUrl is not null) telemetry["ServerUrl"] = serverUrl;
        return new JsonObject
        {
            ["Version"] = version,
            ["Telemetry"] = telemetry
        };
    }

    [Theory]
    [InlineData("https://seatflow.work/api/app/telemetry")]
    [InlineData("https://seatflow.work/api/app/telemetry/")]
    [InlineData("  https://SEATFLOW.WORK/api/app/Telemetry  ")]
    public void Migrate_LegacyDefault_ShouldRewriteToNewDefault(string legacyUrl)
    {
        var result = new AppSettingsMigrators.Step_1_1_to_1_2().Migrate(MakeSettings(legacyUrl));

        result["Telemetry"]!["ServerUrl"]!.GetValue<string>().Should().Be(SeatFlowEndpoints.TelemetryUrl);
        result["Version"]!.GetValue<string>().Should().Be("1.2");
    }

    [Theory]
    [InlineData("https://example.com/collect")]                  // 用户自定义端点
    [InlineData("https://seatflow.work/api/app/note/")]          // 指向 apex 的其它路径：不是旧默认，动它风险太大
    [InlineData("https://api.seatflow.work/api/app/telemetry")]  // 已是新默认
    public void Migrate_NonLegacyValue_ShouldBePreserved(string url)
    {
        var result = new AppSettingsMigrators.Step_1_1_to_1_2().Migrate(MakeSettings(url));

        result["Telemetry"]!["ServerUrl"]!.GetValue<string>().Should().Be(url);
        result["Version"]!.GetValue<string>().Should().Be("1.2");
    }

    [Fact]
    public void Migrate_CamelCaseDocument_ShouldAlsoRewrite()
    {
        // .seatsets 归档/手改文件可能出现 camelCase 形态
        var root = new JsonObject
        {
            ["version"] = "1.1",
            ["telemetry"] = new JsonObject { ["serverUrl"] = "https://seatflow.work/api/app/telemetry" }
        };

        var result = new AppSettingsMigrators.Step_1_1_to_1_2().Migrate(root);

        result["telemetry"]!["serverUrl"]!.GetValue<string>().Should().Be(SeatFlowEndpoints.TelemetryUrl);
        result["version"]!.GetValue<string>().Should().Be("1.2");
    }

    [Fact]
    public void Migrate_MissingTelemetrySection_ShouldOnlyBumpVersion()
    {
        var root = new JsonObject { ["Version"] = "1.1" };

        var result = new AppSettingsMigrators.Step_1_1_to_1_2().Migrate(root);

        result["Telemetry"].Should().BeNull();
        result["Version"]!.GetValue<string>().Should().Be("1.2");
    }

    [Fact]
    public void Migrate_NonStringServerUrl_ShouldBePreserved()
    {
        var root = new JsonObject
        {
            ["Version"] = "1.1",
            ["Telemetry"] = new JsonObject { ["ServerUrl"] = 42 }
        };

        var result = new AppSettingsMigrators.Step_1_1_to_1_2().Migrate(root);

        result["Telemetry"]!["ServerUrl"]!.GetValue<int>().Should().Be(42);
    }

    [Fact]
    public void Step_1_0_to_1_1_ShouldOnlyBumpVersion()
    {
        var root = MakeSettings("https://seatflow.work/api/app/telemetry", version: "1.0");

        var result = new AppSettingsMigrators.Step_1_0_to_1_1().Migrate(root);

        result["Version"]!.GetValue<string>().Should().Be("1.1");
        // 该步不动遥测地址，地址由 1.1 → 1.2 处理
        result["Telemetry"]!["ServerUrl"]!.GetValue<string>().Should().Be("https://seatflow.work/api/app/telemetry");
    }

    [Fact]
    public void MigrationChain_1_0FileWithLegacyUrl_ShouldEndAt1_2WithNewUrl()
    {
        var root = MakeSettings("https://seatflow.work/api/app/telemetry", version: "1.0");
        var migration = new FileMigrationService(
        [
            new AppSettingsMigrators.Step_1_0_to_1_1(),
            new AppSettingsMigrators.Step_1_1_to_1_2()
        ]);

        var result = migration.Migrate("appSettings", root, "1.0", "1.2");

        result["Version"]!.GetValue<string>().Should().Be("1.2");
        result["Telemetry"]!["ServerUrl"]!.GetValue<string>().Should().Be(SeatFlowEndpoints.TelemetryUrl);
    }
}
