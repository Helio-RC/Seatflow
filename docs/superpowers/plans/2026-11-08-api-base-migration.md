# 客户端 API 基址迁移到 `api.seatflow.work` 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把客户端所有后端 API 调用从 apex `https://seatflow.work/api/` 收敛到单一基址 `https://api.seatflow.work/api/`，并为存量用户迁移已持久化的旧遥测地址。

**Architecture:** 在 `SeatFlow.Core` 新增唯一地址来源 `SeatFlowEndpoints`（`ApiBase` + 三个派生常量），四处调用点改为引用它；`AppSettings.json` 走 `appSettings` 1.1 → 1.2 迁移器把「仍是旧默认值」的 `telemetry.serverUrl` 改写为新域名；`main.js` 是静态资产，保留一处独立字面量并注释互指。

**Tech Stack:** .NET 10（`SeatFlow.slnx`）、Avalonia 12.1.4、xUnit v3 + Microsoft.Testing.Platform（`global.json` 的 `test.runner`）、FluentAssertions。

**Spec:** `docs/superpowers/specs/2026-11-08-api-base-migration-design.md`

## Global Constraints

- 对话、代码注释、提交信息一律中文。
- **只改本仓库**（`/projects/Seatflow`）；网页仓库（`SeatFlow_Websites`）任何文件都不改。
- 地址唯一来源是 `SeatFlow.Core.SeatFlowEndpoints`；除 `src/SeatFlow.Browser/wwwroot/main.js`（静态资产，无法引用 C# 常量）外，源码里不得再出现 `seatflow.work/api` 字面量。`download.seatflow.work`（Velopack 更新源）不动。
- 只换域名，**不改请求路径**：`ArrangementCounterService` 里 `"/api/counters/token?name=arrangements"`、`"/api/counters/public/increment"` 保持原样（前导斜杠会覆盖基址路径段，不会产生 `/api/api/…`）。
- 构建：`dotnet build`；测试：`dotnet test --project <测试工程> --filter "FullyQualifiedName~<类名>"`（SDK 10 原生 MTP 模式，此写法已实测可用；不要加 `--`）。
- 每个任务一次提交；提交信息沿用仓库的 conventional commits 风格（`feat(scope): …` / `docs: …`）。

---

### Task 1: 新增 `SeatFlowEndpoints` 单一地址来源

**Files:**
- Create: `src/SeatFlow.Core/SeatFlowEndpoints.cs`
- Test: `tests/SeatFlow.Core.Tests/SeatFlowEndpointsTests.cs`

**Interfaces:**
- Consumes: 无
- Produces:
  - `SeatFlow.Core.SeatFlowEndpoints.ApiBase` → `const string`，值 `"https://api.seatflow.work/api/"`（**带尾斜杠**，可直接作 `HttpClient.BaseAddress`）
  - `SeatFlowEndpoints.TelemetryUrl` → `const string`，`ApiBase + "app/telemetry"`
  - `SeatFlowEndpoints.ReleaseNotesUrl` → `const string`，`ApiBase + "app/note/"`
  - `SeatFlowEndpoints.OnlineLaunchUrl` → `const string`，`ApiBase + "app/online/launch"`

- [ ] **Step 1: 写失败的测试**

创建 `tests/SeatFlow.Core.Tests/SeatFlowEndpointsTests.cs`（命名空间 `SeatFlow.Core.Tests` 嵌套在 `SeatFlow.Core` 内，故无需 `using`）：

```csharp
namespace SeatFlow.Core.Tests;

public class SeatFlowEndpointsTests
{
    [Fact]
    public void ApiBase_ShouldPointToBackendHostWithTrailingSlash()
    {
        SeatFlowEndpoints.ApiBase.Should().Be("https://api.seatflow.work/api/");
        SeatFlowEndpoints.ApiBase.Should().EndWith("/");
    }

    [Fact]
    public void DerivedEndpoints_ShouldComposeFromApiBase()
    {
        SeatFlowEndpoints.TelemetryUrl.Should().Be("https://api.seatflow.work/api/app/telemetry");
        SeatFlowEndpoints.ReleaseNotesUrl.Should().Be("https://api.seatflow.work/api/app/note/");
        SeatFlowEndpoints.OnlineLaunchUrl.Should().Be("https://api.seatflow.work/api/app/online/launch");
    }

    [Fact]
    public void CounterPath_ResolvedAgainstApiBase_ShouldNotDuplicateApiSegment()
    {
        // 计数器用带前导斜杠的路径（覆盖基址路径段）——必须仍是 /api/counters/…，不能变成 /api/api/counters/…
        var uri = new Uri(new Uri(SeatFlowEndpoints.ApiBase), "/api/counters/token?name=arrangements");

        uri.ToString().Should().Be("https://api.seatflow.work/api/counters/token?name=arrangements");
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test --project tests/SeatFlow.Core.Tests/SeatFlow.Core.Tests.csproj --filter "FullyQualifiedName~SeatFlowEndpointsTests"`
Expected: 编译失败，报 `CS0103: 当前上下文中不存在名称"SeatFlowEndpoints"`

- [ ] **Step 3: 写实现**

创建 `src/SeatFlow.Core/SeatFlowEndpoints.cs`：

```csharp
namespace SeatFlow.Core;

/// <summary>
/// 后端 API 端点。**地址唯一来源**，勿在别处硬编码域名。<br/>
/// 客户端全部请求走 <see cref="ApiBase"/>；apex（seatflow.work）只是过渡期的 Worker 反代，不是客户端入口。
/// </summary>
public static class SeatFlowEndpoints
{
    /// <summary>API 基址（带尾斜杠，可直接作为 HttpClient.BaseAddress）。</summary>
    public const string ApiBase = "https://api.seatflow.work/api/";

    /// <summary>遥测上报端点。</summary>
    public const string TelemetryUrl = ApiBase + "app/telemetry";

    /// <summary>版本更新说明端点（后接版本号）。</summary>
    public const string ReleaseNotesUrl = ApiBase + "app/note/";

    /// <summary>在线版（WASM）启动计数端点。</summary>
    public const string OnlineLaunchUrl = ApiBase + "app/online/launch";
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test --project tests/SeatFlow.Core.Tests/SeatFlow.Core.Tests.csproj --filter "FullyQualifiedName~SeatFlowEndpointsTests"`
Expected: `total: 3, failed: 0, succeeded: 3`

- [ ] **Step 5: 提交**

```bash
git add src/SeatFlow.Core/SeatFlowEndpoints.cs tests/SeatFlow.Core.Tests/SeatFlowEndpointsTests.cs
git commit -m "feat(core): 新增 SeatFlowEndpoints 统一后端 API 基址"
```

---

### Task 2: 四个调用点改用 `SeatFlowEndpoints`

**Files:**
- Modify: `src/SeatFlow.Core/Models/AppSettings.cs:78`
- Modify: `src/SeatFlow.Presentation.Avalonia/Services/ArrangementCounterService.cs:1-13`（加 using）、`:31-34`（删常量）、`:41`（用常量）
- Modify: `src/SeatFlow.Desktop/UpdateService.cs:1-12`（加 using）、`:28`
- Modify: `src/SeatFlow.Browser/wwwroot/main.js:15-17`
- Test: `tests/SeatFlow.Core.Tests/AppSettingsTelemetryDefaultsTests.cs`
- Test: `tests/SeatFlow.Presentation.Tests/ArrangementCounterServiceEndpointTests.cs`

**Interfaces:**
- Consumes: `SeatFlowEndpoints.ApiBase` / `.TelemetryUrl` / `.ReleaseNotesUrl`（Task 1）
- Produces: `ArrangementCounterService.BaseAddress` → `internal Uri`（测试用，暴露 `_httpClient.BaseAddress`）

- [ ] **Step 1: 写失败的测试（Core）**

创建 `tests/SeatFlow.Core.Tests/AppSettingsTelemetryDefaultsTests.cs`：

```csharp
namespace SeatFlow.Core.Tests;

public class AppSettingsTelemetryDefaultsTests
{
    [Fact]
    public void TelemetryServerUrl_Default_ShouldUseEndpoints()
    {
        var settings = new AppSettings();

        settings.Telemetry.ServerUrl.Should().Be(SeatFlowEndpoints.TelemetryUrl);
    }
}
```

- [ ] **Step 2: 写失败的测试（Presentation）**

创建 `tests/SeatFlow.Presentation.Tests/ArrangementCounterServiceEndpointTests.cs`：

```csharp
using FluentAssertions;
using SeatFlow.Core;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Tests;

public class ArrangementCounterServiceEndpointTests
{
    [Fact]
    public void Constructor_ShouldUseBackendApiBase()
    {
        using var service = new ArrangementCounterService();

        service.BaseAddress.AbsoluteUri.Should().Be(SeatFlowEndpoints.ApiBase);
        service.BaseAddress.Host.Should().Be("api.seatflow.work");
    }

    [Fact]
    public void CounterPaths_ShouldResolveUnderApiPrefix()
    {
        using var service = new ArrangementCounterService();

        new Uri(service.BaseAddress, "/api/counters/token?name=arrangements").ToString()
            .Should().Be("https://api.seatflow.work/api/counters/token?name=arrangements");
        new Uri(service.BaseAddress, "/api/counters/public/increment").ToString()
            .Should().Be("https://api.seatflow.work/api/counters/public/increment");
    }
}
```

- [ ] **Step 3: 跑测试确认失败**

Run: `dotnet test --project tests/SeatFlow.Core.Tests/SeatFlow.Core.Tests.csproj --filter "FullyQualifiedName~AppSettingsTelemetryDefaultsTests"`
Expected: FAIL，断言实际值为 `"https://seatflow.work/api/app/telemetry"`

Run: `dotnet test --project tests/SeatFlow.Presentation.Tests/SeatFlow.Presentation.Tests.csproj --filter "FullyQualifiedName~ArrangementCounterServiceEndpointTests"`
Expected: 编译失败，报 `CS0117: "ArrangementCounterService"未包含"BaseAddress"的定义`

- [ ] **Step 4: 改 `AppSettings` 默认值**

`src/SeatFlow.Core/Models/AppSettings.cs:78` —— 替换：

```csharp
        public string ServerUrl { get; set; } = "https://seatflow.work/api/app/telemetry";
```

为：

```csharp
        public string ServerUrl { get; set; } = SeatFlowEndpoints.TelemetryUrl;
```

（`AppSettings` 在 `SeatFlow.Core.Models` 命名空间，外层命名空间 `SeatFlow.Core` 内的 `SeatFlowEndpoints` 可直接解析，无需 `using`。）

- [ ] **Step 5: 改 `ArrangementCounterService`**

`src/SeatFlow.Presentation.Avalonia/Services/ArrangementCounterService.cs`，三处：

1) 在 `using Microsoft.Extensions.Logging.Abstractions;` 之后新增一行 `using SeatFlow.Core;`

2) 把第 31-34 行的常量替换为测试钩子：

```csharp
    /// <summary>
    /// 计数器 API 基址。
    /// </summary>
    internal const string BaseUrl = "https://seatflow.work/api/";
```

改为：

```csharp
    /// <summary>计数器 API 基址（测试用；唯一来源是 <see cref="SeatFlowEndpoints.ApiBase"/>）。</summary>
    internal Uri BaseAddress => _httpClient.BaseAddress!;
```

3) 构造函数里 `BaseAddress = new Uri(BaseUrl),` 改为 `BaseAddress = new Uri(SeatFlowEndpoints.ApiBase),`

- [ ] **Step 6: 改 `UpdateService`**

`src/SeatFlow.Desktop/UpdateService.cs`，两处：

1) 在 `using Microsoft.Extensions.Logging;` 之后新增一行 `using SeatFlow.Core;`

2) 第 28 行替换：

```csharp
    private const string ReleaseNotesApiBase = "https://seatflow.work/api/app/note/";
```

为：

```csharp
    private const string ReleaseNotesApiBase = SeatFlowEndpoints.ReleaseNotesUrl;
```

（同一文件第 26 行的 `UpdateApiBase = "https://download.seatflow.work/"` **保持不动**。）

- [ ] **Step 7: 改浏览器静态资产**

`src/SeatFlow.Browser/wwwroot/main.js` 第 15-17 行替换：

```js
// 在线版启动计数 API：运行时资源加载成功后上报一次 downloads 计数。
// 与排座计数一样直接指向官网 API；本地开发/UI 测试（loopback）不上报，避免污染线上统计。
const ONLINE_LAUNCH_API = 'https://seatflow.work/api/app/online/launch';
```

为：

```js
// 在线版启动计数 API：运行时资源加载成功后上报一次 downloads 计数。
// 本地开发/UI 测试（loopback）不上报，避免污染线上统计。
// 地址唯一来源是 src/SeatFlow.Core/SeatFlowEndpoints.cs 的 OnlineLaunchUrl；
// 静态资产无法共享 C# 常量，改地址时两处必须同步。
const ONLINE_LAUNCH_API = 'https://api.seatflow.work/api/app/online/launch';
```

- [ ] **Step 8: 跑测试确认通过**

Run: `dotnet test --project tests/SeatFlow.Core.Tests/SeatFlow.Core.Tests.csproj --filter "FullyQualifiedName~AppSettingsTelemetryDefaultsTests"`
Expected: `total: 1, failed: 0, succeeded: 1`

Run: `dotnet test --project tests/SeatFlow.Presentation.Tests/SeatFlow.Presentation.Tests.csproj --filter "FullyQualifiedName~ArrangementCounterServiceEndpointTests"`
Expected: `total: 2, failed: 0, succeeded: 2`

- [ ] **Step 9: 全解编译（Desktop 与 Browser 两个壳）**

Run: `dotnet build`
Expected: `已成功生成`，0 error（`UpdateService`/`main.js` 所在工程都参与编译）

- [ ] **Step 10: 提交**

```bash
git add src/SeatFlow.Core/Models/AppSettings.cs \
        src/SeatFlow.Presentation.Avalonia/Services/ArrangementCounterService.cs \
        src/SeatFlow.Desktop/UpdateService.cs \
        src/SeatFlow.Browser/wwwroot/main.js \
        tests/SeatFlow.Core.Tests/AppSettingsTelemetryDefaultsTests.cs \
        tests/SeatFlow.Presentation.Tests/ArrangementCounterServiceEndpointTests.cs
git commit -m "feat(client): 遥测/计数/更新说明/在线版计数改指 api.seatflow.work"
```

---

### Task 3: `appSettings` 1.1 → 1.2 迁移器

**Files:**
- Create: `src/SeatFlow.Infrastructure/Migration/Migrators/AppSettingsMigrators.cs`
- Modify: `src/SeatFlow.Infrastructure/Migration/file_versions.json`（`appSettings` → `1.2`，用 `scripts/version.py bump-file` 自动同步）
- Modify: `src/SeatFlow.Core/Models/AppSettings.cs:11`（`Version` 默认值 → `1.2`，同一脚本自动改）
- Modify: `src/SeatFlow.Application/Services/ServiceCollectionExtensions.cs:154-155`（注册迁移器）
- Test: `tests/SeatFlow.Infrastructure.Tests/Migration/AppSettingsMigratorsTests.cs`
- Test: `tests/SeatFlow.Infrastructure.Tests/Repositories/JsonAppSettingsRepositoryTests.cs`（追加 3 个用例）

**Interfaces:**
- Consumes: `SeatFlowEndpoints.TelemetryUrl`（Task 1）
- Produces: `SeatFlow.Infrastructure.Migration.Migrators.AppSettingsMigrators.Step_1_1_to_1_2`（`IFileMigrator`，`FileType = "appSettings"`，`FromVersion = "1.1"`，`ToVersion = "1.2"`）

- [ ] **Step 1: 写失败的迁移器测试**

创建 `tests/SeatFlow.Infrastructure.Tests/Migration/AppSettingsMigratorsTests.cs`：

```csharp
using System.Text.Json.Nodes;
using SeatFlow.Core;
using SeatFlow.Infrastructure.Migration.Migrators;

namespace SeatFlow.Infrastructure.Tests.Migration;

public class AppSettingsMigratorsTests
{
    private static JsonObject MakeSettings(string? serverUrl)
    {
        var telemetry = new JsonObject();
        if (serverUrl is not null) telemetry["serverUrl"] = serverUrl;
        return new JsonObject
        {
            ["version"] = "1.1",
            ["telemetry"] = telemetry
        };
    }

    [Theory]
    [InlineData("https://seatflow.work/api/app/telemetry")]
    [InlineData("https://seatflow.work/api/app/telemetry/")]
    [InlineData("  https://SEATFLOW.WORK/api/app/Telemetry  ")]
    public void Migrate_LegacyDefault_ShouldRewriteToNewDefault(string legacyUrl)
    {
        var result = new AppSettingsMigrators.Step_1_1_to_1_2().Migrate(MakeSettings(legacyUrl));

        result["telemetry"]!["serverUrl"]!.GetValue<string>().Should().Be(SeatFlowEndpoints.TelemetryUrl);
        result["version"]!.GetValue<string>().Should().Be("1.2");
    }

    [Theory]
    [InlineData("https://example.com/collect")]                  // 用户自定义端点
    [InlineData("https://seatflow.work/api/app/note/")]          // 指向 apex 的其它路径：不是旧默认，动它风险太大
    [InlineData("https://api.seatflow.work/api/app/telemetry")]  // 已是新默认
    public void Migrate_NonLegacyValue_ShouldBePreserved(string url)
    {
        var result = new AppSettingsMigrators.Step_1_1_to_1_2().Migrate(MakeSettings(url));

        result["telemetry"]!["serverUrl"]!.GetValue<string>().Should().Be(url);
        result["version"]!.GetValue<string>().Should().Be("1.2");
    }

    [Fact]
    public void Migrate_MissingTelemetrySection_ShouldOnlyBumpVersion()
    {
        var root = new JsonObject { ["version"] = "1.1" };

        var result = new AppSettingsMigrators.Step_1_1_to_1_2().Migrate(root);

        result["telemetry"].Should().BeNull();
        result["version"]!.GetValue<string>().Should().Be("1.2");
    }

    [Fact]
    public void Migrate_NonStringServerUrl_ShouldBePreserved()
    {
        var root = new JsonObject
        {
            ["version"] = "1.1",
            ["telemetry"] = new JsonObject { ["serverUrl"] = 42 }
        };

        var result = new AppSettingsMigrators.Step_1_1_to_1_2().Migrate(root);

        result["telemetry"]!["serverUrl"]!.GetValue<int>().Should().Be(42);
    }

    [Fact]
    public void Migrate_PascalCaseKey_ShouldRewrite()
    {
        var root = new JsonObject
        {
            ["version"] = "1.1",
            ["telemetry"] = new JsonObject { ["ServerUrl"] = "https://seatflow.work/api/app/telemetry" }
        };

        var result = new AppSettingsMigrators.Step_1_1_to_1_2().Migrate(root);

        result["telemetry"]!["ServerUrl"]!.GetValue<string>().Should().Be(SeatFlowEndpoints.TelemetryUrl);
    }
}
```

- [ ] **Step 2: 追加仓储集成测试**

在 `tests/SeatFlow.Infrastructure.Tests/Repositories/JsonAppSettingsRepositoryTests.cs` 末尾的类中（`SettingsFilePath_ShouldReturnCorrectPath` 之后）追加以下三个用例，并在文件顶部 `using SeatFlow.Infrastructure.Migration;` 之后补一行 `using SeatFlow.Infrastructure.Migration.Migrators;`：

```csharp
    [Fact]
    public async Task LoadAsync_LegacyTelemetryUrl_ShouldMigrateToApiHost()
    {
        var path = GetTempFilePath();
        try
        {
            await File.WriteAllTextAsync(path, """
            {
              "version": "1.1",
              "telemetry": { "serverUrl": "https://seatflow.work/api/app/telemetry", "enabled": true }
            }
            """);
            var repo = new JsonAppSettingsRepository(path,
                new FileMigrationService([new AppSettingsMigrators.Step_1_1_to_1_2()]));

            var settings = await repo.LoadAsync(CancellationToken.None);

            settings.Telemetry.ServerUrl.Should().Be("https://api.seatflow.work/api/app/telemetry");
            settings.Telemetry.Enabled.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_CustomTelemetryUrl_ShouldBePreserved()
    {
        var path = GetTempFilePath();
        try
        {
            await File.WriteAllTextAsync(path, """
            { "version": "1.1", "telemetry": { "serverUrl": "https://example.com/collect" } }
            """);
            var repo = new JsonAppSettingsRepository(path,
                new FileMigrationService([new AppSettingsMigrators.Step_1_1_to_1_2()]));

            var settings = await repo.LoadAsync(CancellationToken.None);

            settings.Telemetry.ServerUrl.Should().Be("https://example.com/collect");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_NewerFileVersion_ShouldStillLoad()
    {
        // 老版本客户端读到 1.2 文件不应报错（FileMigrationService 只向前迁移）
        var path = GetTempFilePath();
        try
        {
            await File.WriteAllTextAsync(path, """
            { "version": "1.2", "telemetry": { "serverUrl": "https://example.com/collect" } }
            """);
            var repo = new JsonAppSettingsRepository(path,
                new FileMigrationService([new AppSettingsMigrators.Step_1_1_to_1_2()]));

            var settings = await repo.LoadAsync(CancellationToken.None);

            settings.Telemetry.ServerUrl.Should().Be("https://example.com/collect");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
```

- [ ] **Step 3: 跑测试确认失败**

Run: `dotnet test --project tests/SeatFlow.Infrastructure.Tests/SeatFlow.Infrastructure.Tests.csproj --filter "FullyQualifiedName~AppSettingsMigratorsTests"`
Expected: 编译失败，报 `CS0246: 未能找到类型或命名空间名"AppSettingsMigrators"`

- [ ] **Step 4: 写迁移器实现**

创建 `src/SeatFlow.Infrastructure/Migration/Migrators/AppSettingsMigrators.cs`：

```csharp
using System.Text.Json.Nodes;
using SeatFlow.Core;

namespace SeatFlow.Infrastructure.Migration.Migrators;

/// <summary>
/// AppSettings.json 各版本迁移器。
/// </summary>
public static class AppSettingsMigrators
{
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

            if (rootObject["telemetry"] is JsonObject telemetry)
            {
                var key = telemetry.ContainsKey("serverUrl") ? "serverUrl"
                    : telemetry.ContainsKey("ServerUrl") ? "ServerUrl"
                    : null;

                if (key is not null
                    && telemetry[key] is JsonValue value
                    && value.TryGetValue<string>(out var url)
                    && IsLegacyDefault(url))
                {
                    telemetry[key] = SeatFlowEndpoints.TelemetryUrl;
                }
            }

            rootObject["version"] = "1.2";
            return rootObject;
        }

        /// <summary>忽略首尾空白、尾随斜杠与大小写差异后，判断是否等于旧默认地址。</summary>
        internal static bool IsLegacyDefault(string? url)
            => Normalize(url) == Normalize(LegacyTelemetryUrl);

        private static string Normalize(string? url)
            => (url ?? string.Empty).Trim().TrimEnd('/').ToUpperInvariant();
    }
}
```

- [ ] **Step 5: 升版本戳（脚本自动同步 Model 默认值）**

Run: `python3 scripts/version.py bump-file appSettings --set 1.2`
Expected: 输出两行变更 —— `file_versions.json: 1.1 → 1.2` 与 `AppSettings.cs: 1.1 → 1.2`

验证：

Run: `grep -n '"appSettings"' src/SeatFlow.Infrastructure/Migration/file_versions.json && grep -n 'public string Version' src/SeatFlow.Core/Models/AppSettings.cs`
Expected: 两处都显示 `1.2`

- [ ] **Step 6: 注册迁移器**

`src/SeatFlow.Application/Services/ServiceCollectionExtensions.cs` 第 154-155 行之后新增一行：

```csharp
            services.AddSingleton<IFileMigrator, AppSettingsMigrators.Step_1_1_to_1_2>();
```

（同文件需要 `using SeatFlow.Infrastructure.Migration.Migrators;` —— 先检查文件顶部是否已有；`VenueMigrators`/`SeatSetsMigrators` 已在用，故应已存在，无需新增。）

- [ ] **Step 7: 跑测试确认通过**

Run: `dotnet test --project tests/SeatFlow.Infrastructure.Tests/SeatFlow.Infrastructure.Tests.csproj --filter "FullyQualifiedName~AppSettingsMigratorsTests"`
Expected: `total: 9, failed: 0, succeeded: 9`（两个 Theory 各 3 个 InlineData + 3 个 Fact）

Run: `dotnet test --project tests/SeatFlow.Infrastructure.Tests/SeatFlow.Infrastructure.Tests.csproj --filter "FullyQualifiedName~JsonAppSettingsRepositoryTests"`
Expected: `total: 6, failed: 0, succeeded: 6`（原 3 个 + 新增 3 个）

- [ ] **Step 8: 提交**

```bash
git add src/SeatFlow.Infrastructure/Migration/Migrators/AppSettingsMigrators.cs \
        src/SeatFlow.Infrastructure/Migration/file_versions.json \
        src/SeatFlow.Core/Models/AppSettings.cs \
        src/SeatFlow.Application/Services/ServiceCollectionExtensions.cs \
        tests/SeatFlow.Infrastructure.Tests/Migration/AppSettingsMigratorsTests.cs \
        tests/SeatFlow.Infrastructure.Tests/Repositories/JsonAppSettingsRepositoryTests.cs
git commit -m "feat(migration): AppSettings 1.1→1.2 迁移遥测地址到 api.seatflow.work"
```

---

### Task 4: 文档同步

**Files:**
- Modify: `docs/adr/ADR-009-arrangement-counter.md:11`、`:62`
- Modify: `docs/WebDeployment.md:56`
- Modify: `CHANGELOG.md`（`## [Unreleased]` 下新增 `### Changed` 段）

**Interfaces:**
- Consumes: 无
- Produces: 无（纯文档）

- [ ] **Step 1: 改 ADR-009 两处**

`docs/adr/ADR-009-arrangement-counter.md:11` —— `（`seatflow.work/api/counters`）` 改为 `（`api.seatflow.work/api/counters`）`。

`:62` —— `- `BaseAddress` 带尾部斜杠：`"https://seatflow.work/api/"`` 改为：

```markdown
- `BaseAddress` 带尾部斜杠：`"https://api.seatflow.work/api/"`（唯一来源 `SeatFlow.Core.SeatFlowEndpoints.ApiBase`）
```

- [ ] **Step 2: 改 WebDeployment**

`docs/WebDeployment.md:56` 的这两行：

```markdown
- 运行时资源加载成功后，`main.js` 向 `https://seatflow.work/api/app/online/launch` 上报一次
  下载计数（`downloads` 计数器 +1，与官网下载页共用统计）；`no-cors` fire-and-forget，
```

替换为：

```markdown
- 运行时资源加载成功后，`main.js` 向 `https://api.seatflow.work/api/app/online/launch` 上报一次
  下载计数（`downloads` 计数器 +1，与官网下载页共用统计）；`no-cors` fire-and-forget，
  该地址唯一来源是 `src/SeatFlow.Core/SeatFlowEndpoints.cs` 的 `OnlineLaunchUrl`（`main.js` 是静态资产，
  无法共享 C# 常量，改地址时两处必须同步）；
```

- [ ] **Step 3: 补 CHANGELOG**

在 `CHANGELOG.md` 的 `## [Unreleased]` 段里，`### Added` 块之后、`### Fixed` 之前插入：

```markdown
### Changed
- **客户端 API 基址迁到 `api.seatflow.work`**：遥测、排座计数、版本更新说明与在线版启动计数统一由 `SeatFlow.Core.SeatFlowEndpoints` 提供，不再依赖 apex 的 `/api/*` Worker 反代（该反代保留到旧版本用户减少）；新增 `AppSettings` 1.1 → 1.2 迁移器，只把仍是旧默认值的 `telemetry.serverUrl` 改写为新域名，用户自定义地址保留
```

- [ ] **Step 4: 复查残留**

Run:

```bash
grep -rn "seatflow\.work/api" --include="*.cs" --include="*.js" --include="*.md" src/ docs/ scripts/ .github/ \
  | grep -v "/obj/\|/bin/" \
  | grep -v "api\.seatflow\.work/api" \
  | grep -v "docs/superpowers/specs/2026-11-08-api-base-migration-design.md"
```

Expected: 只剩 `src/SeatFlow.Infrastructure/Migration/Migrators/AppSettingsMigrators.cs` 里的
`LegacyTelemetryUrl` 常量（历史默认值，必须保留）——命中 1 条即正确；其它任何命中都说明漏改。

- [ ] **Step 5: 提交**

```bash
git add docs/adr/ADR-009-arrangement-counter.md docs/WebDeployment.md CHANGELOG.md
git commit -m "docs: 同步 API 基址迁移（ADR-009 / WebDeployment / CHANGELOG）"
```

---

### Task 5: 全量验证

**Files:** 无改动（只读验证；发现问题回到对应任务修）

**Interfaces:**
- Consumes: Task 1–4 的全部产出
- Produces: 验证结论

- [ ] **Step 1: 全解构建**

Run: `dotnet build`
Expected: `警告 0 / 错误 0`（原有警告除外），构建成功

- [ ] **Step 2: 相关测试全跑**

Run: `dotnet test --project tests/SeatFlow.Core.Tests/SeatFlow.Core.Tests.csproj --filter "FullyQualifiedName~SeatFlowEndpointsTests|FullyQualifiedName~AppSettingsTelemetryDefaultsTests"`
Expected: `failed: 0`

（若该 `|` 组合过滤不被支持，改为分两条命令各跑一次。）

Run: `dotnet test --project tests/SeatFlow.Presentation.Tests/SeatFlow.Presentation.Tests.csproj --filter "FullyQualifiedName~ArrangementCounterServiceEndpointTests"`
Expected: `failed: 0`

Run: `dotnet test --project tests/SeatFlow.Infrastructure.Tests/SeatFlow.Infrastructure.Tests.csproj --filter "FullyQualifiedName~AppSettingsMigratorsTests"`
Expected: `failed: 0`

- [ ] **Step 3: 线上冒烟（新域名直连后端）**

Run:

```bash
for u in "https://api.seatflow.work/api/counters/token?name=arrangements" "https://api.seatflow.work/api/app/note/2.3.0"; do
  printf '%-70s ' "$u"; curl -s --max-time 10 -o /dev/null -w '%{http_code}\n' "$u"
done
```

Expected: 两行均为 `200`

- [ ] **Step 4: 人工确认（无法在本仓库验证的项）**

- 桌面端启动后，日志/抓包中遥测与计数请求的 host 为 `api.seatflow.work`；
- 在线版（`online.seatflow.work`）跨域仍会因后端 `App:CorsOrigins` 缺少该源而静默失败——属网页仓库待办，不是本计划缺陷；
- apex `seatflow.work/Docs` 在主页迁到 Worker 后是否仍可达（Q8 遗留确认项）。

- [ ] **Step 5: 汇报**

汇总：改动文件列表、测试结果数字、冒烟状态码、遗留待办（后端 CORS、`/Docs` 可达性）。
本任务不产生提交；若验证中发现缺陷，修复后并入对应任务的提交（或单独 `fix(client): …` 提交）。
