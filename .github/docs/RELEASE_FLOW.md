# SeatFlow CI / 发布流水线说明

本项目发布已迁移至 GitHub Actions：自动发布由 `version.json` 驱动，手动发布通过
`workflow_dispatch` 触发；增量更新包（delta）、Worker 密钥轮换均由 CI 管理。

## 工作流一览

| 工作流 | 触发 | 职责 |
|--------|------|------|
| `release.yml` | push `version.json`（自动）/ workflow_dispatch（手动） | **仅构建**：预检 → 4 RID（win-x64 / linux-x64 / osx-x64 / osx-arm64）并行。稳定版：restore 历史缓存 → OSS 同步 → vpk 打包（delta）、预发布：拉最新 full；暂存 artifacts |
| `publish.yml` | workflow_run（release.yml 成功且 push 触发，自动）/ workflow_dispatch（手动） | **仅发布**：下载 artifacts → 版本校验 → OSS 上传（仅自动）→ GitHub Release（自动 latest / 手动永远 pre-release） |
| `publish-web.yml` | workflow_run（publish.yml 成功且为 push 自动链路） | **在线版发布（仅正式版）**：WASM 构建 → OSS `online_worktable/<version>/` 上传 + 完整性校验 → KV `current` 切换 → 冒烟 → 清理旧版本 |
| `unit-tests.yml` | push/pull_request（代码变更） | 构建 + 分层单元测试（无缓存，直接 restore） |
| `worker-secret-sync.yml` | 每周一 03:00 UTC / 手动 | 将 OSS 密钥同步到 Cloudflare Worker（secrets-bulk） |

> 构建与发布完全解耦：构建失败不会产生任何 Release；发布可独立重跑（手动指定
> `release.yml` 的 `run_id`），不受构建矩阵影响。

## 一、自动发布（latest）

1. 修改根目录 `version.json` 的 `version` 字段（如 `2.0.0` → `2.0.1`），建议使用
   `python3 scripts/version.py bump-app patch --force` 统一管理
2. 同步更新 `RELEASE.md` 发布说明（GitHub Release body 直接读取该文件）与
   `CHANGELOG.md`
3. 提交并合并到 `main` → `release.yml` 因 `version.json` 变更自动触发：
   - 预检：该 tag 不存在，且版本号必须大于当前最新 release，否则中止（失败不触发构建）
   - 并行构建 4 个平台：win-x64（Setup.exe）/ linux-x64（AppImage）/
     osx-x64 + osx-arm64（.dmg），同时生成增量更新包（`-delta.nupkg`）
4. 构建成功后 `publish.yml`（workflow_run）自动接力：
   - 上传 OSS（安装包 → `releases/{version}/`，更新包 → `updates/`，索引 → `releases/releases.json`）
   - 创建 GitHub Release（**latest**，非 pre-release）

## 二、手动发布（pre-release，不传 OSS）

1. 进入 GitHub 仓库 → **Actions → Release SeatFlow（构建）→ Run workflow**，填写参数：
   - `version`（必填）：发布版本号，**仅用于本次构建与 tag**，不写回 `version.json`
   - `suffix`（可选）：如 `beta.1`、`rc`，最终版本为 `{version}-{suffix}`
2. 构建成功（artifacts 已上传）后 → **Actions → Publish SeatFlow（发布）→ Run workflow**：
   - `run_id`（可选）：本次 `release.yml` 构建运行的 ID，留空默认取最近成功的手动构建
   - `version`（必填）：与构建时一致
   - `suffix`（可选）：与构建时一致
3. 手动发布固定为 **pre-release**，且**不执行 OSS 上传**（仅 GitHub Release）
4. 同样受预检约束：tag 不得已存在、版本必须大于当前最新 release

> 构建与发布分离：构建结束只是产出 artifacts；是否发布、发布成 latest 还是
> pre-release 由 `publish.yml` 单独决定（自动路径仅 push 构建后接 latest）。

## 三、在线版（Web/WASM）发布

仅正式版：`version.json`（不含 `-`）变更触发 `release.yml`，其成功后的 `publish.yml`
再成功后，`publish-web.yml` 自动接力：

1. 构建 `src/SeatFlow.Browser`（必须 `wasm-tools`，否则原生库不链接、部署白屏）
2. 上传 `online_worktable/<version>/`（原文件 + `.br`，跳过 `.gz`/`.map`）并做 key/大小全量校验
3. 校验通过后写 KV `current=<version>`（原子切换）；预发布与手动 publish 不进入本流程
4. 冒烟断言 `X-Online-Version`（容忍 KV 传播约 5 分钟），随后清理旧版本（保留最近 5 个，current 永不删除）

回滚（KV 秒级切换，需 CF 凭证）：

```bash
CF_ACCOUNT_ID=... CF_API_TOKEN=... \
python3 scripts/ci/upload_web_oss.py --version <旧版本号> --switch-only
```

## 四、增量更新包（delta）

稳定版构建在 `vpk pack` 前通过 `scripts/ci/sync_velopack_history.py` 以 OSS 为唯一真源
同步打包历史到 `publish/history`：保留全部历史 delta + 最新 2 个 full，并做双向对账
（下载缺失/大小不符者；删除本地不在保留集的 nupkg——含 OSS 上不存在的未发布版本与超出窗口的旧 full）。`vpk pack --outputDir publish/history`
据此生成 `SeatFlow-{version}-{rid}-delta.nupkg`，并让 `releases.{rid}.json` 携带完整
delta 链（客户端按版本顺序串联，超过 10 跳或 delta 总大小超过 full 时退回全量下载）。

`publish/history` 通过 `actions/cache` 跨运行缓存（仅加速）；缓存失效时自动从 OSS
全量回填。同步失败硬失败（不发版）；缓存保存失败不影响发布（`continue-on-error`）。

预发布（workflow_dispatch）不参与同步与缓存，仍由 `scripts/ci/fetch_previous.sh`
（封装 `vpk download http`）拉取最新 full 作为 delta 基础，失败容错跳过。

> 历史注意：2026-09 之前 Worker 对 nupkg 直链返回 403，`fetch_previous` 一直静默失败，
> 因此 OSS 上 1.4.x / 2.0.0 没有任何 delta；链路自 2.1.0 起积累（不回填历史缺口，
> 缺口仅在同步日志中列出）。

## 五、密钥轮换（Worker）

`worker-secret-sync.yml` 每周一 03:00 UTC 自动运行（亦可手动触发），
调用 `scripts/ci/rotate_worker_secrets.py`，将 `OSS_KEY_ID/OSS_KEY_SECRET`
以 Worker 实际读取的绑定名 `OSS_ACCESS_KEY_ID` / `OSS_ACCESS_KEY_SECRET`
同时下发到 `oss-proxy` 与 `online_worktable`（`CF_WORKER_SCRIPT` 支持逗号分隔
多个脚本，两个 Worker 共用同一把密钥）。失败自动创建 `ops` 标签 Issue。

## 六、所需 Secrets / Vars 配置

| 名称 | 类型 | 用途 |
|------|------|------|
| `UPDATE_FEED_URL` | var | 预发布 delta 基础（`vpk download http` 更新源）；稳定版已由 OSS 同步取代 |
| `OSS_KEY_ID` / `OSS_KEY_SECRET` | secret | OSS 访问密钥（上传 + Worker 同步源） |
| `OSS_ENDPOINT` / `OSS_BUCKET` | var | OSS 地址（桌面分发与在线版共用；Worker 回源桶为 `seatflow-download`） |
| `CF_ACCOUNT_ID` / `CF_API_TOKEN` | secret | Cloudflare API 访问（Token 需含 `Workers Scripts: Edit` + `Workers KV Storage: Edit`） |
| `CF_WORKER_SCRIPT` | var | 承载密钥的 Worker 脚本名，支持逗号分隔多个（如 `oss-proxy,online_worktable`） |
| `CF_API_BASE` | var（可选） | Cloudflare API 基址（默认 `https://api.cloudflare.com`；填完整 `/client/v4` 基址亦可） |
| `VPK_KEY_ID` / `VPK_KEY_FILE` / `VPK_KEY_PASSWORD` | secret（可选） | 代码签名（`--keyId`/`--keyFile`/`--keyPassword`），**任一为空即跳过签名环节**；Windows 用 pfx 证书，macOS 用 p12 |

`release.yml` 的 build job 使用**仓库级** `OSS_KEY_ID/OSS_KEY_SECRET`（secrets）与
`OSS_ENDPOINT/OSS_BUCKET`（vars）做历史同步（不引用 Environment）；`publish.yml`
仍在 Environment `OSS` 中执行上传（该环境 branch policy 仅允许 `main`）。预发布不访问 OSS。

> 在线版 KV 命名空间 id 内置在 `upload_web_oss.py`（`online_worktable`），
> 如需覆盖可设置环境变量 `CF_KV_NAMESPACE_ID`，无需新增仓库变量。

### 签名环节说明

`vpk pack` 步骤仅在以下 secrets **全部非空**时附加签名参数：

```yaml
if [ -n "$VPK_KEY_ID" ]; then ARGS+=(--keyId "$VPK_KEY_ID"); fi
if [ -n "$VPK_KEY_FILE" ]; then ARGS+=(--keyFile "$VPK_KEY_FILE"); fi
if [ -n "$VPK_KEY_PASSWORD" ]; then ARGS+=(--keyPassword "$VPK_KEY_PASSWORD"); fi
```

未配置时产物为未签名包（GitHub 分发可直接安装，Windows 可能出现 SmartScreen 提示）。

### vpk 系统依赖

- **Linux runner**（win-x64 / linux-x64 打包）：`squashfs-tools` + `zstd`（workflow 已 apt 安装）
- **macOS runner**（dmg 打包）：`zstd`（workflow 已 brew 安装），vpk 工具链随 `dotnet tool install -g vpk` 就绪

### 并行构建矩阵

| matrix.rid | os | vpk 指令 | 产物 |
|-----------|-----|---------|------|
| win-x64 | ubuntu-latest | `[win]` | Setup.exe + nupkg（跨平台打包） |
| linux-x64 | ubuntu-latest | `[linux]` | AppImage + nupkg |
| osx-x64 / osx-arm64 | macos-latest | `[osx]` | .dmg + nupkg（macOS 专用托盘，需 macOS 主机执行） |

矩阵 `fail-fast: false`：单个平台失败不阻塞其余平台，
但 `release` job 仍会整体失败（需全部成功后才发版）。

## 七、缓存策略

- `unit-tests.yml` 不再使用 NuGet 缓存（避免 2.5 GB 级条目反复堆积挤占仓库缓存配额）
- `release.yml`（稳定版 build job）缓存 `publish/history`（Velopack 打包历史）：
  key `vpk-history-{rid}-{run_id}-{run_attempt}`，restore-keys 前缀 `vpk-history-{rid}-`；
  每次运行保存新 key（滚动）；保存失败（超限等）不阻塞发布，由 OSS 同步兜底
- 历史目录只保留 nupkg 与 channel 文件；安装包与 `assets.*.json` 在暂存后即被清理

## 八、脚本约定（scripts/ci/）

| 脚本 | 职责 |
|------|------|
| `upload_oss.py` | 上传产物至 OSS（凭据全部来自环境变量，无硬编码 URL/密钥） |
| `upload_web_oss.py` | 在线版上传/完整性校验/KV 切换/旧版本清理（凭据全部来自环境变量） |
| `rotate_worker_secrets.py` | Cloudflare secrets-bulk 同步（多 Worker；CF API 基址可通过 `CF_API_BASE` 覆盖） |
| `fetch_previous.sh` | 封装 `vpk download http` 拉取上版本（仅预发布 delta 基础，容错） |
| `sync_velopack_history.py` | 以 OSS 为真源同步 Velopack 打包历史（全部 delta + 最新 2 full，双向对账，缺口报告） |
| `stage_velopack_artifacts.sh` | 暂存本次发布产物到 `publish/dist`；稳定版从历史目录清理安装包 |

所有 URL / 账号 / 渠道标识均通过 `vars` / `secrets` 注入，脚本与工作流内无硬编码。
客户端应用内 `UpdateService.UpdateApiBase` 为运行时常量，如需统一参数化请另行处理。
