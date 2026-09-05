# SeatFlow Web 版设计：Avalonia WASM 双壳架构

- 日期：2026-09-05
- 状态：已确认（用户批准分支开发）
- 目标分支：`feat/avalonia-web`

## 1. 目标与范围

在现有 Avalonia 12 桌面应用基础上添加浏览器呈现层：**教师免安装、功能等价**的 Web 部署版本。数据存浏览器本地（IndexedDB），提供备份包导出/导入。桌面端行为零回归（双壳隔离）。

### 非目标

- 多用户协作/云同步/账号体系（后续可再议）
- xlsx 导入（若 Phase 0 验证失败则 Web 版不支持）、PDF 导出（默认降级）
- 移动端适配（浏览器版目标为桌面浏览器，不做布局重构）

## 2. 架构：共享库 + 双壳（官方 xplat 形态）

```
SeatFlow.Core / Application / Infrastructure   → TargetFrameworks: net10.0;net10.0-browser
SeatFlow.Presentation.Avalonia                → 改为共享类库（移除 OutputType/app.manifest/Desktop 包），net10.0;net10.0-browser
SeatFlow.Desktop（新，EXE）                    → net10.0：旧 Program.cs 全部迁入（Velopack/StartupGuard/Watchdog/zenity/Process）
SeatFlow.Browser（新）                         → net10.0-browser：JSHost 导入、App 启动、wwwroot/index.html
```

- 依赖方向不变：Desktop/Browser → Presentation → Application → Infrastructure/Core。浏览器壳不引用任何桌面包。
- `App.axaml.cs` 启动序列保留在共享层；桌面专用逻辑仅在 Desktop 壳。App 类内部用 `#if DESKTOP`/`#if BROWSER` 小分叉（约 50 行），避免拆两套 App 造成主题/资源重复。
- `.slnx` 增加 2 项目；`scripts/build/publish.sh` 增加 browser 档（`dotnet publish -f net10.0-browser -c Release`，无 RID，产物 `wwwroot`）。
- 桌面端 `dotnet run` 目标项目变为 `SeatFlow.Desktop`。

## 3. 数据存储抽象（核心改动）

### 3.1 接口

Core 新增 `ILocalDataStore`（异步签名，IndexedDB 只有异步 JS API）：

```csharp
Task<string?> ReadTextAsync(string relativePath);
Task WriteTextAsync(string relativePath, string content);
Task DeleteAsync(string relativePath);
Task<bool> ExistsAsync(string relativePath);
Task EnsureDirectoryAsync(string relativePath);
Task<IReadOnlyList<string>> ListAsync(string relativeDir);
```

### 3.2 实现（Infrastructure 内 `#if BROWSER` 条件编译，依赖方向不变）

- `net10.0` → `FileSystemDataStore`：包装现有 System.IO（根目录仍由 `AppEnvironment.DefaultDataDirectory` 计算），桌面行为零变化。
- `net10.0-browser` → `IndexedDbDataStore`：`[JSImport]` 调 IndexedDB（key = 相对路径），JSON 仓库、迁移服务、ContentHash 校验照常运行。

### 3.3 仓库改造

各 Repository（JsonVenueRepository、RosterRepository、JsonAppSettingsRepository、StrategyConfigFileRepository、SeatingSnapshotRepository、JsonStudentDatasetRepository）从静态 `AppEnvironment` + 同步 `File.*` 改为构造注入 `ILocalDataStore` + `await` 调用。依赖注入注册在 Application 层 `ServiceCollectionExtensions`，每平台壳只注册各自 DataStore 实现。

### 3.4 备份包

序列化格式、迁移器、ContentHash、CircularHistory 全部不受影响。备份 = 导出全部 JSON 为 `.seatsets`（或全量包）Blob 下载；恢复 = 现有导入路径复用。

## 4. 平台服务适配

| 服务 | 桌面（现状保留） | 浏览器（新增） |
|---|---|---|
| `IFileService` | StorageProvider 对话框 | JS interop：`<input type=file>` → `byte[]`；保存 → Blob URL 下载 |
| `IDialogService` | `ShowDialog` 模态 Window | Overlay：提炼 `DialogContent` UserControl，MainWindow 顶层遮罩面板（官方明示 WASM 无独立窗口） |
| `IUpdateService` | Velopack | 不注册；Settings 页 `#if BROWSER` 隐藏更新区块 |
| Watchdog / StartupGuard | Desktop 壳启动 | 不启动 |
| `Process.Start`（打开链接等） | 原样 | 抽象 `IUrlOpener` → `window.open` JSImport，改动 Home/About/Settings/MemberManagement 4 处 |

剪贴板、文件拖放、CanvasZoomPan 滚轮：Phase 0 验证；不可用则降级（拖放仅是入口便捷）。

共享层受影响改动：`DialogWindow`/`InputWindow` 提炼内容（约 100 行）。

## 5. 导入导出定档

- **导入**：CSV/JSON 全平台。XLSX：Phase 0 验证 EPPlus `net10.0-browser`；失败则 `CompositeStudentProvider` 成员改由 DI 注入注册表，Web 端不含 XLSX（提示"Excel 另存为 CSV"）。
- **导出器重构为返回 `byte[]`**：`ExportBytesAsync` 新增，桌面壳写文件、浏览器壳触发下载。4 个 exporter 机械式改造。
- **图片导出**：Phase 0 确认 ImageSeatingExporter 实现方式；若 SkiaSharp 直绘则浏览器版用 `RenderTargetBitmap` 重构。
- **PDF（QuestPDF）**：默认降级（Web 不提供），不投入设计成本。

## 6. UI 差异 + 发布

- **字体**：WASM 无系统字体。全局 `FontFamily` 改 `fonts:Inter#Inter` 形式。
- **裁剪 vs ViewLocator 反射**：browser csproj 配 `TrimRoot` 保 SeatFlow 程序集。
- **Setup**：`dotnet workload install wasm-tools`；Browser 壳 `AppBuilder.ConfigureWeb()` + `JSHost.ImportAsync` 加载 interop 模块。
- **Settings 降级**：隐藏"打开数据目录/更新检查"，改为"导出备份包/导入备份包"。
- **文档**：新增 `docs/WebDeployment.md`；更新 `docs/INDEX.md`。

## 7. 阶段计划（每阶段独立提交）

### Phase 0 — 技术验证 spike（1-2 天）
1. `dotnet workload install wasm-tools`
2. `/tmp` 生成最小 `avalonia.xplat` 项目（CommunityToolkit），确认 Desktop+Browser 双壳可构建、browser 可发布
3. 验证：EPPlus 、QuestPDF、SkiaSharp 在 net10.0-browser 编译/运行行为（导入 xlsx → 导出字节）
4. 验证：模态 Window/ShowDialog 在浏览器实际行为（预期不可用 → 确认 overlay 路线）
5. 验证：文件拖放/剪贴板/CanvasZoomPan 在浏览器可用性
6. 结论文档：定档清单（哪些能力 Web 可用/降级）

### Phase 1 — 结构拆分 + ILocalDataStore（1.5-2 周）
1. Core 增加 `ILocalDataStore`；Infrastructure 实现双平台 DataStore（`#if BROWSER`），FileSystemDataStore 包装现有路径
2. Core/Application/Infrastructure 多 TFM `net10.0;net10.0-browser`
3. Presentation 改共享库（去掉 EXE 属性/Desktoped 引用）；新建 Desktop/Browser 壳（先空壳可构建）
4. 全仓库注入 ILocalDataStore 改造 + Application DI 接入
5. 验证：桌面测试套件全绿；browser 空壳能构建

### Phase 2 — 平台服务适配（1 周）
1. DialogContent 提炼 + Browser 端 overlay DialogService/InputService
2. IFileService 浏览器实现（input file / blob 下载）
3. IUrlOpener 抽象 + 4 处 Process.Start 改造
4. `#if BROWSER` 隐藏 Settings 更新区块/数据目录入口
5. IUpdateService/Watchdog 隔离到 Desktop

### Phase 3 — 导出/导入字节化 + UI 差异（1 周）
1. 4 个 exporter 增加 `ExportBytesAsync`
2. 图片导出浏览器方案（按 Phase 0 结论）
3. 字体 `fonts:Inter#Inter` 全局替换
4. Browser 壳 TrimRoot 配置 + 首轮 `dotnet publish -f net10.0-browser` 出包验证

### Phase 4 — 发布管线与文档（3-5 天）
1. publish.sh browser 档 + release.py 扩展
2. docs/WebDeployment.md + docs/INDEX.md + RELEASE.md 更新
3. 端到端走查：部署静态站、验证全流程（导入/排座/保存/导出/备份）

## 8. 风险

| 风险 | 缓解 |
|---|---|
| EPPlus/QuestPDF WASM 不可用 | Phase 0 定档；降级路径已设计 |
| IndexedDB 桥接性能（大量小文件） | 单 key 存 JSON 时按目录聚合读写（Phase 2 调优，若 Phase 0 测出瓶颈） |
| WASM 性能（策略执行慢 2-4 倍） | 门控阈值：200 人级名单在 10s 内可接受收；超限时 Profile 优化 |
| 裁剪误删反射类型 | TrimRoot 保 SeatFlow 程序集 |
| 桌面回归 | 双壳隔离 + 测试套件守护；桌面壳不触碰任何逻辑 |
