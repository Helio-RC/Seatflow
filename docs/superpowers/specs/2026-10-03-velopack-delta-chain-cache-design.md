# SeatFlow Velopack 增量链 + Action 缓存 + OSS 回填设计

- 日期：2026-10-03
- 状态：已确认（Q1–Q12 访谈通过），待用户审阅
- 目标分支：`main`（本地领先 origin/main 13 个提交，2.1.0 尚未发布，首个正式版将走新链路）

## 1. 背景与问题

### 1.1 现状

- 发布流水线：`release.yml`（preflight → 4 RID 并行构建 → `scripts/ci/fetch_previous.sh` 用 `vpk download http` 只拉最新一个 full → `vpk pack --delta BestSpeed` → artifacts）→ `publish.yml`（下载 artifacts → OSS → GitHub Release）。
- 客户端：标准 Velopack `UpdateManager` + `SimpleWebSource`（`src/SeatFlow.Desktop/UpdateService.cs:354-361`，源 `https://download.seatflow.work/updates/`）。
- OSS `updates/`：历次 `-full.nupkg` 全部保留（`upload_oss.py` 对 nupkg put-if-new），**零 `-delta.nupkg`**。

### 1.2 根因（证据）

2.0.0 构建日志（run 35426729455 / win-x64）：

```
Found 1 release(s) in remote file
Downloading 'SeatFlow-1.4.1-win-x64-full.nupkg' from '.../updates/SeatFlow-1.4.1-win-x64-full.nupkg'
ERR Response status code does not indicate success: 403 (Forbidden)，重试 3 次失败
[warn] 拉取历史产物失败（首次发布或无历史），本次仅生成 full 包
```

Worker 当时对 nupkg 直链返回 403 → `fetch_previous.sh` 容错退出（exit 0）→ `vpk pack` 无上一版 full → 不生成 delta。当前直链已恢复 200（实测 `vpk download` 成功拉取 2.0.0 full），但历史无任何 delta。

### 1.3 Velopack 关键语义（v1.2.0 源码 + 本地实测确认）

- `vpk pack` 只生成**一个** delta：「outputDir 中版本低于新版本的最大 Full」→ 新版本；
- 写出的 `releases.{channel}.json` 会列出 outputDir 内**所有** nupkg（顺序：版本降序，同版本按类型）；
- 客户端 `CreateDeltaUpdateStrategy` 取 feed 中「版本 > 本机 full 且 ≤ 目标」的所有 delta，按版本顺序**串联应用**；超过 `MaximumDeltasBeforeFallback=10` 或 delta 总大小超过 full 时退回全量；
- 客户端需要本地 full 包作为链的起点（Windows 安装器自带，Linux/macOS 首次全量更新后获得）。

结论：**打包目录保留历史 delta ⇒ feed 携带完整链 ⇒ 落后多版用户可增量更新**。命名已实测：`SeatFlow-{version}-{channel}-full.nupkg` / `-delta.nupkg`（版本可含 `-` 后缀；安装包为 `SeatFlow-{channel}-Setup.exe`，不带版本）。

### 1.4 版本错配

客户端 `Velopack` 包 = `1.2.161`，CI 安装与 `dotnet-tools.json` 的 vpk = `1.2.0`，2.0.0 构建日志出现 `Velopack library version is greater than vpk version ... may become a fatal error` 警告。

## 2. 目标与非目标

### 目标

1. 正式版打包历史持续保留（Action 缓存加速 + OSS 回填），feed 携带完整 delta 链；
2. 缓存仅作加速，OSS 为唯一真源：每次稳定版构建与 OSS 双向对账；
3. 同步失败硬失败；缓存保存 best-effort（失败不阻塞发布）；
4. 释放 NuGet 缓存占用；vpk 与客户端库版本对齐。

### 非目标

- 不回填 1.4.x / 2.0.0 的历史 delta（仅检测并报告缺口）；
- 不改客户端 `UpdateService`、不改 Worker、不清理 OSS 归档；
- 预发布（`workflow_dispatch`）流程保持现状。

## 3. 决策记录（访谈结论）

| 编号 | 决定 |
|---|---|
| Q1 | 目标 = 多跳增量链 |
| Q2/Q8 | 保留全部历史 delta（不设 K 上限）+ 最近 2 个 full；OSS 归档不删；冷启动按此窗口回填 |
| Q3 | 每次稳定版构建 restore + OSS 双向对账（下载缺失；删除本地 OSS 上不存在的 nupkg，防止未发布版本进入 feed） |
| Q4/Q10 | build job 使用仓库级 OSS secrets（`OSS_KEY_ID/SECRET` 已存在），**不**加 `environment: OSS`（其 branch policy 仅允许 `main`） |
| Q5 | 预发布保持现状：`publish/out` + `fetch_previous.sh`，不参与缓存 |
| Q6 | `publish/history`（缓存）与 `publish/dist`（artifacts）分离，只暂存本次产物 |
| Q7 | 增加 actionlint 工作流 + 同步脚本 pytest + 手动验证 |
| Q9 | 移除 `release.yml` / `unit-tests.yml` 的 NuGet 缓存并删除现有条目 |
| Q11 | 同步失败硬失败（不发版） |
| Q12' | 不回填历史 delta，仅检测报告 |
| 追加 | CI 钉 vpk `1.2.161`（与客户端包一致），同步更新 `dotnet-tools.json` |

## 4. 架构与数据流

```
稳定版 build job（每个 matrix rid 独立执行）:
  checkout → setup-dotnet → setup-python
    → actions/cache/restore@v6            # publish/history
    → pip install oss2 packaging
    → python3 scripts/ci/sync_velopack_history.py \
          --channel <rid> --output-dir publish/history
         ├─ 列举 OSS updates/
         ├─ 过滤本通道 nupkg → 保留集 = 全部 delta + 最新 2 个 full
         ├─ 下载缺失（size + MD5/ETag 校验）到 publish/history
         ├─ 删除本地存在但不在保留集的 *.nupkg
         └─ 缺口报告（有 full 无指向它的 delta → warn 列表）
    → dotnet publish -o publish/tmp
    → vpk pack --outputDir publish/history --delta BestSpeed
    → scripts/ci/stage_velopack_artifacts.sh:
         本次 full/delta nupkg + releases.*.json + RELEASES-* + 安装包 → publish/dist
         稳定版：从 history 删除安装包与 assets.*.json（缓存只留 nupkg/feed）
    → actions/cache/save@v6 (continue-on-error: true)
    → upload-artifact: publish/dist/* （artifact 名 release-<rid> 不变）

预发布 build job（is_pre=true）:
  delta 基础与现状一致（fetch_previous → publish/out → pack），
  另走统一暂存步骤（stage → publish/dist → 上传），
  不 restore/sync/save 缓存。

publish.yml（零改动）:
  download-artifact → 预检 → upload_oss.py（nupkg put-if-new；feed 覆盖）
  → GitHub Release（仅安装包资产）
```

## 5. 组件设计

### 5.1 `scripts/ci/sync_velopack_history.py`（新增）

**CLI / 环境**

```
python3 scripts/ci/sync_velopack_history.py \
  --channel <rid> --output-dir <dir> [--pack-id SeatFlow] [--full-keep 2] [--dry-run]
```

环境变量：`OSS_KEY_ID` / `OSS_KEY_SECRET` / `OSS_ENDPOINT` / `OSS_BUCKET`（与 `upload_oss.py` 一致）。
依赖：`oss2`、`packaging`（build job 执行 `pip install oss2 packaging`）。

**算法**

1. `oss2.ObjectIteratorV2(bucket, prefix="updates/")` 分页列举，收集 `key/size/etag`；
2. 按正则精确匹配本通道包：`^{pack_id}-(?P<ver>.+)-{channel}-(?P<kind>full|delta)\.nupkg$`（channel 先 `re.escape`；`ver` 贪婪回溯由结尾锚定），版本用 `packaging.version.Version` 校验，解析失败仅 warn 跳过；
3. 保留集 = **所有 delta** + 按版本降序的前 `full-keep`（默认 2）个 full；
4. 双向对账：
   - 保留集内本地缺失或大小不符 → 下载至 `<name>.incomplete`，校验 size，若 OSS ETag 为 32 位 hex 则校验 MD5，通过后原子改名；
   - 本地 `*.nupkg` 不在保留集 → 删除（含其他通道、未发布版本、被裁掉的旧 full）；
   - 非 `.nupkg` 文件一律不碰（feed 由 pack 重生成）；
5. 缺口报告（基于 OSS 列举，不受本地裁剪影响）：对每个 full 版本（除最早一个），检查是否存在指向它的 delta，缺失版本号列表输出 warn（不重建）；
6. 汇总日志：列举/下载/删除/保留/缺口数量与字节数；`--dry-run` 只打印计划、不做任何本地/远端变更；
7. 任何 OSS/IO 错误 → 打印明确错误并非零退出（Q11 硬失败）；空 OSS（首次）视为正常空集。

**边界**

- 幂等；对 OSS 只读；不处理安装包/portable/`assets.*.json`；
- 预发布绝不调用；
- 对同一版本既有 full 又有 delta 属正常；
- 支持带 `-` 后缀的预发布版本名（如 `2.2.0-beta.1`），但正式链路不产生。

### 5.2 `scripts/ci/stage_velopack_artifacts.sh`（新增）

用法：`stage_velopack_artifacts.sh <src-dir> <dist-dir> <version> <channel> <is-pre>`

- `set -euo pipefail` + `shopt -s nullglob`；
- 复制安装包：`*.exe` / `*.AppImage` / `*.pkg` / `*.dmg`（pack 每次覆盖同名 → 天然仅当前版本）；
- 按精确名复制本次 nupkg：`SeatFlow-<version>-<channel>-full.nupkg`、`...-delta.nupkg`（存在才复制，首次/预发布可能无 delta）；
- 复制 channel 文件：`releases.*.json`、`RELEASES-*`；
- **排除** `assets.*.json`；
- `is-pre != true`（稳定版）时，从 src 删除除 `*.nupkg`、`releases.*.json`、`RELEASES-*` 之外的全部文件（安装包、Portable.zip、`assets.*.json`），使缓存只保留 nupkg + feed；
- 输出 dist 文件清单日志；feed 文件缺失视为错误。

### 5.3 `.github/workflows/release.yml`

| 项目 | 改动 |
|---|---|
| NuGet 缓存 | 删除该步骤 |
| vpk 安装 | `dotnet tool install -g vpk --version 1.2.161` |
| 历史缓存 restore | `actions/cache/restore@v6`（`fail-on-cache-miss` 保持默认 false），`if: needs.preflight.outputs.is_pre != 'true'`；path `publish/history`；key `vpk-history-${{ matrix.rid }}-${{ github.run_id }}-${{ github.run_attempt }}`；restore-keys `vpk-history-${{ matrix.rid }}-` |
| 同步 | `if` 同上；`pip install oss2 packaging`；`sync_velopack_history.py --channel ${{ matrix.rid }} --output-dir publish/history`；env 用 `secrets.OSS_KEY_ID/OSS_KEY_SECRET` + `vars.OSS_ENDPOINT/OSS_BUCKET` |
| fetch_previous | 加 `if: needs.preflight.outputs.is_pre == 'true'`（仅预发布） |
| pack | `--outputDir` 用 `${{ needs.preflight.outputs.is_pre == 'true' && 'publish/out' || 'publish/history' }}`（env `VPK_OUTPUT_DIR`）；其余参数（签名、icon、`--noPortable`、`--delta BestSpeed`）不变 |
| 暂存 | 调用 `stage_velopack_artifacts.sh "$VPK_OUTPUT_DIR" publish/dist "${{ needs.preflight.outputs.version }}" "${{ matrix.rid }}" "${{ needs.preflight.outputs.is_pre }}"` |
| 历史缓存 save | `actions/cache/save@v6`，同 key，`if` 同上，`continue-on-error: true` |
| upload-artifact | path 改为 `publish/dist/*`；artifact 名 `release-${{ matrix.rid }}` 不变 |
| timeout | build job 45 → 60 分钟（仅冷启动回填更久） |

不新增 `environment:`；`permissions`、concurrency、preflight 均不变。

### 5.4 `.github/workflows/unit-tests.yml`

仅删除 NuGet 缓存步骤（`actions/cache@v6`），其余不变。

### 5.5 `.github/workflows/actionlint.yml`（新增）

- 触发：`push` / `pull_request`，`paths: ['.github/workflows/**']`；
- 权限：`contents: read`；
- 步骤：检出 → 按技能要求下载钉版 actionlint `1.7.7` 并 `sha256sum -c` 校验 → `./actionlint -shellcheck= -pyflakes= -color .github/workflows/*.yml`。

### 5.6 `dotnet-tools.json`

`vpk` 版本 `1.2.0` → `1.2.161`（`rollForward: false` 不变）。

### 5.7 NuGet 缓存清理（实施步骤，非代码）

删除仓库现有 `Linux-nuget-*` 缓存条目（4 条，合计约 10.4 GB）：`gh cache list` 查出 id 后 `gh cache delete` 逐条删除（或 `gh cache delete --all`）。CodeQL 条目（约 65 MB）可保留。

## 6. 错误处理与边界

| 场景 | 行为 |
|---|---|
| OSS 列举/下载/密钥失败（稳定版） | 脚本非零退出 → job 失败 → 不发版（Q11） |
| 缓存 restore miss / key 过期 | 同步从 OSS 全量回填（冷启动） |
| 缓存 save 失败（超限等） | `continue-on-error`，不影响发布；下次冷启动回填 |
| 缓存含未发布版本（publish 曾失败） | 双向对账在 pack 前删除 → feed 不会引用 404 文件 |
| 重试同一版本（缓存已含该版本） | 对账删除未发布版本 → pack 重建 |
| 历史缺口（如 1.4.0→1.4.1） | 仅 warn 报告；对应旧客户端首次升级走全量 |
| 只有 1 个 full（新通道/首次） | 保留集仅该 full，无 delta，feed 正常 |
| 预发布 | 完全不触碰缓存与同步；OSS 不参与 |

## 7. 验证计划

1. `cd scripts && python3 -m pytest tests/ -v`（含新增 `test_sync_velopack_history.py`：保留集计算、缺失下载、多余删除、缺口报告、失败退出、`--dry-run`）；
2. actionlint 工作流在 PR 上通过（并本地对 `.github/workflows/*.yml` 跑一次）；
3. 本地真 OSS `--dry-run`（用户提供凭证）核对计划：当前预期 = 0 个 delta + 每通道最新 2 个 full（win/linux 只有 2.0.0 一个 full；macOS 只有 2.0.0），缺口报告列出 1.4.1 等；
4. 删除现有 `Linux-nuget-*` 缓存；
5. 推送 `main` 触发 2.1.0（首个正式版）：
   - sync 冷启动日志（4 通道各下载 2.0.0 full，约 75–80 MB/通道）；
   - pack 生成 `SeatFlow-2.1.0-<rid>-delta.nupkg`；
   - cache save 成功；artifacts 只含本次文件；
6. feed 校验：`curl https://download.seatflow.work/updates/releases.win-x64.json` 资产 = `2.1.0 full` + `2.1.0 delta` + `2.0.0 full` + `1.4.1 full`（保留集含 OSS 最新 2 个 full：2.0.0 与 1.4.1）；
7. 再发一版：确认缓存命中（sync 仅列举/HEAD，无重复下载）；
8. 可选客户端冒烟：在 2.0.0 安装版检查更新，确认走增量（更新包远小于 full）。

## 8. 文档联动

实施前先读 `docs/INDEX.md` 并按规则执行，预计涉及：

- `.github/docs/RELEASE_FLOW.md`：工作流表、第四节（delta 链改为 OSS 同步 + 历史保留）、第六节（build 用仓库级密钥；预发布不变）、第七节（移除 NuGet 缓存、新增 `vpk-history-*`）、第八节（脚本表新增 2 个脚本）；
- `scripts/ToolsCollection.md`：`sync_velopack_history.py`、`stage_velopack_artifacts.sh` 条目；
- `CHANGELOG.md`：对应条目（2.1.0 / Unreleased，按实施时章节定）；
- `AGENTS.md` / `docs/INDEX.md`：若索引或脚本清单需要同步则更新。

## 9. 风险与回滚

- **Worker 403 属既有风险**：客户端更新依赖 Worker 直链且无 GitHub 兜底，本次不变更；建议后续单独监控（超范围，仅记录）。
- **仓库级 OSS 密钥有效性**：同步强依赖；若仓库级密钥与环境密钥不一致/失效，首次运行会硬失败（预期行为，需先核对/更新）。
- **feed 只列最近 2 个 full**：客户端回退只下载最新 full，旧 full 不再分发（符合 Velopack 语义）；全部历史 delta 仍在。
- **缓存上限**：移除 NuGet 缓存后，历史缓存为主要占用；仅 delta（小）增长，超限时 save 降级、由 OSS 回填兜底。
- **冷启动耗时**：2.1.0 首次构建多下载约 300 MB（4 通道 full），已预留 60 分钟 timeout。
- **回滚**：OSS 保留全部 nupkg；必要时可临时恢复 `fetch_previous.sh` 旧链路发版（脚本保留），或在本地用历史目录重新 `vpk pack` 生成并替换 feed。
