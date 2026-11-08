# 客户端 API 基址迁移至 `api.seatflow.work` 设计

- 日期：2026-11-08
- 状态：已确认（Q1–Q10 访谈通过），待用户审阅
- 目标分支：`main`
- 范围：**仅客户端仓库**（`Seatflow`）；网页仓库（`Seatflow_Websites`）本轮不改

## 1. 背景与问题

### 1.1 起因

官网主页即将迁到 Cloudflare Worker 承载。apex（`seatflow.work`）的 `/api/*` 目前由 `site-gateway` Worker
反代到 `.NET` 宿主，是**桌面客户端与在线版 WASM 的 API 基址**；主页迁移后该反代只是过渡措施，
最终会下线。届时客户端不能再把 apex 当 API 入口，必须直连后端域名 `api.seatflow.work`。

决策：**apex → 后端的 `/api/*` 反代保留，直到旧版本（无法自更新的存量客户端）使用者减少到可接受**；
客户端从本版本起改用 `https://api.seatflow.work/api/`。

### 1.2 现状调用点（改前）

| 位置 | 值 | 用途 | 备注 |
|---|---|---|---|
| `src/SeatFlow.Core/Models/AppSettings.cs:78` | `https://seatflow.work/api/app/telemetry` | 遥测 | **写入用户 `AppSettings.json` 持久化** |
| `src/SeatFlow.Presentation.Avalonia/Services/ArrangementCounterService.cs:34` | `https://seatflow.work/api/` | `GET /api/counters/token`、`POST /api/counters/public/increment` | 桌面 + 在线版共用 |
| `src/SeatFlow.Desktop/UpdateService.cs:28` | `https://seatflow.work/api/app/note/` | release notes | 仅桌面 |
| `src/SeatFlow.Browser/wwwroot/main.js:17` | `https://seatflow.work/api/app/online/launch` | 在线版启动计数 | 静态资产，`no-cors` |
| `src/SeatFlow.Desktop/UpdateService.cs:26` | `https://download.seatflow.work/` | Velopack 更新源 | **不动**（独立 Worker） |

站点/文档链接（`Data/about.json` 的 `officialSiteUrl`/`docsUrl`/`quickStartUrl`/`faqUrl`、
`MemberManagementViewModel.cs:547`）**本轮不动**。

### 1.3 事实基线（实测，2026-11-08）

- `https://api.seatflow.work/health` → 200；`/api/counters/token?name=arrangements` → 200 且返回真实 token；
  `/api/app/note/2.1.0` → 200。**路径前缀 `/api` 保留**，后端直接服务，无需 Worker 参与。
- `seatflow.top` / `api.seatflow.top` **无 DNS 记录**——因此本次采用 `seatflow.work` 体系（`api.seatflow.work`）。
- `FileMigrationService` 已支持「文件版本更新则原样返回」，故升到 1.2 的 `AppSettings.json`
  被老客户端读取不会报错（`.seatsets` 归档内嵌 `AppSettings.json` 同样安全）。
- `JsonAppSettingsRepository.LoadAsync` 已接入 `FileMigrationService.Migrate("appSettings", …)`，
  只是目前没有 `appSettings` 迁移器、`file_versions.json` 停在 `1.1`。
- **CORS 缺口**：`api.seatflow.work` 对 `Origin: https://online.seatflow.work` 不返回
  `Access-Control-Allow-Origin`（GET 与 `OPTIONS` 预检均无，重复 3 次稳定）；对 `https://seatflow.work`
  与 `https://www.seatflow.work` 正常返回。即在线版的计数/遥测跨域**当前即被浏览器拦下（静默失败）**，
  与本次换基址无关，需后端补 `App:CorsOrigins`。

## 2. 目标与非目标

### 目标

1. 客户端全部后端 API 调用改为单一基址 `https://api.seatflow.work/api/`，且**只有一处**定义。
2. 存量用户已持久化的旧遥测地址被迁到新地址（仅当其仍是旧默认值）。
3. 相关文档中的地址字面量同步，避免与代码漂移。

### 非目标

- 不改网页仓库：`site-gateway` 路由、`API_ORIGIN`、`PAGES_DEPLOY.md`、`docs/API.md` 等。
- 不改后端配置（`App:CorsOrigins` 补 `online.seatflow.work` 记为后端待办，见 §4）。
- 不改成运行期可配置/远端下发（远端配置本身依赖该域名可用，且现有字段已可手改）。
- 不动站点/文档链接与 Velopack 更新源域名。

## 3. 设计

### 3.1 单一基址：`SeatFlowEndpoints`

新增 `src/SeatFlow.Core/SeatFlowEndpoints.cs`：

```csharp
namespace SeatFlow.Core;

/// <summary>后端 API 端点。唯一来源，勿在别处硬编码域名。</summary>
public static class SeatFlowEndpoints
{
    /// <summary>API 基址（带尾斜杠，可直接作 HttpClient.BaseAddress）。</summary>
    public const string ApiBase = "https://api.seatflow.work/api/";

    public const string TelemetryUrl = ApiBase + "app/telemetry";
    public const string ReleaseNotesUrl = ApiBase + "app/note/";
    public const string OnlineLaunchUrl = ApiBase + "app/online/launch";
}
```

落点理由：`SeatFlow.Core` 同时被 `Application` / `Infrastructure` / `Presentation.Avalonia` / `Desktop`
引用（`Desktop → Presentation → Application → Core`），是唯一能服务全部四个调用点的项目。

### 3.2 调用点改造

| 文件 | 改动 |
|---|---|
| `src/SeatFlow.Core/Models/AppSettings.cs:78` | `ServerUrl { get; set; } = SeatFlowEndpoints.TelemetryUrl;` |
| `src/SeatFlow.Presentation.Avalonia/Services/ArrangementCounterService.cs:34,41` | 删除本地 `BaseUrl` 常量，`BaseAddress = new Uri(SeatFlowEndpoints.ApiBase)` |
| `src/SeatFlow.Desktop/UpdateService.cs:28` | `ReleaseNotesApiBase = SeatFlowEndpoints.ReleaseNotesUrl;` |
| `src/SeatFlow.Browser/wwwroot/main.js:17` | 字面量改为 `'https://api.seatflow.work/api/app/online/launch'`，并加注释指回 `SeatFlowEndpoints` |

**注意（using）**：`SeatFlow.Core.Models` 内可直接解析 `SeatFlowEndpoints`（外层命名空间）；
`Presentation.Avalonia` 与 `Desktop` 的改动点需补 `using SeatFlow.Core;`。

**注意（实现陷阱）**：`ArrangementCounterService` 的请求路径带前导斜杠（`"/api/counters/token?name=arrangements"`、
`"/api/counters/public/increment"`）。前导斜杠会覆盖 `BaseAddress` 的路径段，最终 URL 由请求路径单独决定，
因此不会出现 `/api/api/…`，也**不要**顺手把请求路径改成相对形式（无收益、徒增 diff）。改完用冒烟与单测确认最终
URL 为 `https://api.seatflow.work/api/counters/token`。

`main.js` 无法共享 C# 常量（独立静态资产，不走编译），保留一处字面量并注释互指；不引入构建期注入。

### 3.3 存量配置迁移：`appSettings` 1.1 → 1.2

新增 `src/SeatFlow.Infrastructure/Migration/Migrators/AppSettingsMigrators.cs`：

```csharp
public static class AppSettingsMigrators
{
    private const string LegacyTelemetryUrl = "https://seatflow.work/api/app/telemetry";

    public sealed class Step_1_1_to_1_2 : IFileMigrator
    {
        public string FileType => "appSettings";
        public string FromVersion => "1.1";
        public string ToVersion => "1.2";

        public JsonNode Migrate(JsonNode root)
        {
            // 只改「仍是旧默认值」的 telemetry.serverUrl；用户自定义值一律不动
            // 比较前 Trim() + TrimEnd('/') + OrdinalIgnoreCase
            // 键不存在 / 已是新默认 / 类型不是 string → 原样返回
        }
    }
}
```

规则：

1. 定位 `telemetry` / `Telemetry` 节点（**磁盘上的 `AppSettings.json` 是 PascalCase**——`SaveAsync` 用
   `JsonOptions.WriteIndented`，未设命名策略；camelCase 形态仅出现在手改或 `.seatsets` 归档里），键不存在则不动。
2. 取 `serverUrl` / `ServerUrl` 字符串值，`Trim().TrimEnd('/')` 后与 `LegacyTelemetryUrl` 做 `OrdinalIgnoreCase`
   比较，**相等才改写**为新默认 `SeatFlowEndpoints.TelemetryUrl`。
3. 不改写任何其它字段、不新增字段；版本键写回原有大小写（`Version` 存在则写 `Version`）。

配套：

- 新增 `Step_1_0_to_1_1`（**无结构变更，仅补版本戳**）：1.0 时代的文件没有版本戳，
  不补这一步迁移链会停在 1.0，2 号规则就永远跑不到。
- `src/SeatFlow.Infrastructure/Providers/JsonAppSettingsRepository.cs`：版本读取必须
  `node["version"] ?? node["Version"]`（原来大小写敏感，读到 null 就退化成 1.0，迁移链一直是断的）。
- `src/SeatFlow.Infrastructure/Migration/file_versions.json`：`"appSettings": "1.2"`。
- `src/SeatFlow.Core/Models/AppSettings.cs:11`：`Version` 默认值 `"1.1"` → `"1.2"`。
- `src/SeatFlow.Application/Services/ServiceCollectionExtensions.cs:154-155` 旁注册两个迁移器
  （`Step_1_0_to_1_1`、`Step_1_1_to_1_2`）。
- 保存路径 `JsonAppSettingsRepository.SaveAsync` 已按 `FileVersionInfo.GetCurrentVersion("appSettings")`
  打戳，无需改动。

**为什么值得一个迁移器**（AGENTS.md：「不转换会出错/丢信息」才写）：改默认值对新装用户生效，
但存量用户文件里已写死旧地址，遥测会静默停摆且无任何报错——属于会丢信息的语义变化。
不匹配用户自定义值的理由：`ServerUrl` 是用户可编辑字段，静默改写用户显式配置比漏改更糟。

### 3.4 客户端不做的容错

**不加「新地址失败回退 apex」逻辑**。apex 反代仍在，回退只会把「哪条链路在失败」变得难诊断；
遥测已有熔断/退避，计数器与 note 失败本就不影响主流程。

### 3.5 文档同步

- `docs/adr/ADR-009-arrangement-counter.md:11`（正文 `seatflow.work/api/counters` 表述）与 `:62`
  （`BaseAddress` 字面量）改为 `api.seatflow.work`。
- `docs/WebDeployment.md:56`（`main.js` 上报地址字面量）改为 `api.seatflow.work`。
- `CHANGELOG.md` 追加两条：客户端 API 基址迁移；新增 `appSettings` 1.1 → 1.2 迁移器。

## 4. 边界与跨仓库事项

| 事项 | 归属 | 本轮 |
|---|---|---|
| apex `/api/*` 反代保留到旧版本用户减少 | 网页仓库 `site-gateway` | 不动，仅在本文记录为过渡依赖 |
| `App:CorsOrigins` 补 `https://online.seatflow.work` | 网页仓库后端配置 | **待办**，不做则在线版计数/遥测继续静默失败（现状已如此） |
| `about.json` / 帮助链接指向 apex 的 `/Docs` | 客户端（链接） | 不动；需人工确认主页迁到 Worker 后 `/Docs` 仍可达，若不可达另开小改动 |
| Velopack 更新源 `download.seatflow.work` | 网页仓库 Worker | 不动 |
| `seatflow.top` 域名体系 | — | 不采用（无 DNS），本次统一 `seatflow.work` |

## 5. 测试与验收

1. **迁移器单测**（`tests/SeatFlow.Infrastructure.Tests/Migration/AppSettingsMigratorsTests.cs`）：
   - 旧默认值 → 改写成新默认（PascalCase 文档为主，camelCase 文档另测）；
   - 旧默认的等价变体（尾随斜杠、大小写不同、首尾空白）→ 同样改写为新默认；
   - 用户自定义地址（含指向 apex 的其它路径）→ 原样保留；
   - `Telemetry` 缺失 / `ServerUrl` 缺失 / 非字符串 / 已是新值 → 不改写；
   - 1.0 文件走完整链（1.0 → 1.1 → 1.2）后地址为新默认。
2. **仓储集成**（扩充 `tests/SeatFlow.Infrastructure.Tests/Repositories/JsonAppSettingsRepositoryTests.cs`）：
   1.1 文件加载后 `Telemetry.ServerUrl` 为新地址；自定义值保持；老版本读 1.2 文件不抛错。
3. **构建/回归**：`dotnet build`；`dotnet test --filter "FullyQualifiedName~Migration|FullyQualifiedName~AppSettings|FullyQualifiedName~Counter"`。
4. **在线冒烟**（人工/脚本）：
   - `curl -s -o /dev/null -w '%{http_code}' https://api.seatflow.work/api/counters/token?name=arrangements` → 200（已预验）；
   - `curl -s -o /dev/null -w '%{http_code}' https://api.seatflow.work/api/app/note/<当前版本>` → 200（已预验）；
   - 桌面端启动后观察日志中遥测/计数请求 URL host 为 `api.seatflow.work`。

## 6. 风险与回滚

| 风险 | 影响 | 缓解 |
|---|---|---|
| 后端对 `api.seatflow.work` 的限流/GeoIP 判定与 apex 不同 | 计数/遥测被限 | 该域名原本就是 `site-gateway` 的 `API_ORIGIN`，行为等价；切后观察 429 |
| 在线版跨域失败（无 ACAO） | 在线版计数/遥测继续静默失败 | 现状一致，非本次引入；记为后端待办 |
| 迁移器误改用户自定义值 | 用户遥测打到自己指的地址 | 严格等值匹配，仅改旧默认字面量 |
| 旧客户端读到 1.2 文件 | 兼容性 | `FileMigrationService` 版本更高即原样返回；已核对 |

回滚：全部为常量与版本戳变更，`git revert` 单个提交即可；期间 apex 反代仍在，无切换窗口风险。

## 7. 决策记录

| # | 决策 | 结论 |
|---|---|---|
| Q1 | 域名 | `seatflow.work` 体系：`https://api.seatflow.work/api/`（`.top` 无 DNS，不采用） |
| Q2 | apex `/api/*` | Worker 反代保留到旧版本用户减少 |
| Q3 | 改动范围 | 仅客户端仓库；后端/网页仓库只出待办 |
| Q4 | 寻址方式 | 收敛为单一基址常量（不做运行期可配置） |
| Q5 | 存量配置 | 加 `appSettings` 1.1 → 1.2 迁移器 |
| Q6 | 收敛形态 | `SeatFlow.Core.SeatFlowEndpoints`（`ApiBase` + 三个派生值）；`main.js` 独立常量 |
| Q7 | 迁移匹配宽度 | 窄：仅改等于旧默认的值（`Trim` + `TrimEnd('/')` + 忽略大小写） |
| Q8 | 站点/文档链接 | 本轮不动，`/Docs` 可达性待人工确认 |
| Q9 | 在线版跨域 | 统一指向新 host，CORS 记为后端待办 |
| Q10 | 文档/验收 | 同步 ADR-009、WebDeployment、CHANGELOG；迁移器三情形测试 + 在线冒烟 |
