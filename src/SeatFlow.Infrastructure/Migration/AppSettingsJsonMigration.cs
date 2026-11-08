using System.Text.Json.Nodes;
using SeatFlow.Infrastructure.Migration.Migrators;

namespace SeatFlow.Infrastructure.Migration;

/// <summary>
/// <c>AppSettings.json</c> 的迁移入口。<br/>
/// 有两条读取路径都要迁移，且都不能漏：
/// <list type="bullet">
///   <item><see cref="Providers.JsonAppSettingsRepository"/>：正常设置加载（走 DI 的 <see cref="FileMigrationService"/>）。</item>
///   <item>遥测管线的早期配置读取（Avalonia 侧的 <c>TelemetryServiceCollectionExtensions</c>）：此刻
///   ServiceProvider 还没建好，只能自己构造迁移服务——它决定遥测端点 URL，漏迁移就会继续上报旧域名。</item>
/// </list>
/// 故迁移器清单与「版本号读取（兼容 PascalCase/camelCase）」都收敛在这里。
/// </summary>
public static class AppSettingsJsonMigration
{
    /// <summary>当前 <c>appSettings</c> 文件版本。</summary>
    public static string CurrentVersion => FileVersionInfo.GetCurrentVersion("appSettings");

    /// <summary>构造 <c>appSettings</c> 的完整迁移链（顺序执行）。</summary>
    public static IFileMigrator[] CreateMigrators() =>
    [
        new AppSettingsMigrators.Step_1_0_to_1_1(),
        new AppSettingsMigrators.Step_1_1_to_1_2()
    ];

    /// <summary>
    /// 把 <c>AppSettings.json</c> 文本迁移到 <see cref="CurrentVersion"/>，返回迁移后的 JSON 文本。<br/>
    /// 版本号键名同时兼容 <c>version</c> / <c>Version</c>：磁盘上的文件是 PascalCase，
    /// 大小写敏感会读成 1.0 而让整条迁移链失效。
    /// </summary>
    /// <param name="json">文件原文。</param>
    /// <param name="migration">已注册全部迁移器的服务（DI 路径传入）；为 <c>null</c> 时按 <see cref="CreateMigrators"/> 自建。</param>
    public static string MigrateToCurrent(string json, FileMigrationService? migration = null)
    {
        var node = JsonNode.Parse(json);
        if (node is null)
            return json;

        var fileVersion = JsonKeyCase.Find(node as JsonObject, "version")?.GetValue<string>() ?? "1.0";
        var service = migration ?? new FileMigrationService(CreateMigrators());

        return service.Migrate("appSettings", node, fileVersion, CurrentVersion).ToJsonString();
    }
}
