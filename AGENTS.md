# AGENTS.md

本文件是本仓库 AI 编码代理的唯一主指南（根目录 `CLAUDE.md` 是指向本文件的指针）。
文档地图与「改代码需联动哪些文档」见 `docs/INDEX.md`；架构细节见 `ARCHITECTURE.md`。

## 语言约定

- 对话、思考、代码注释一律使用中文。

## 环境与构建

- 执行 GUI 操作前先确认无头环境（检查 `DISPLAY` / `WAYLAND_DISPLAY`）与 .NET 10 SDK；avdt（Avalonia DevTools）仅桌面可用。

```bash
dotnet build                                   # 构建全部项目（SeatFlow.slnx，需 .NET 10 SDK）
dotnet test                                    # 全部测试（xUnit v3 + Microsoft.Testing.Platform）
dotnet test --filter "FullyQualifiedName~Xxx"  # 单个测试
dotnet run --project src/SeatFlow.Desktop      # 启动桌面应用
```

**`dotnet r` 任务（npm scripts 风格，读取 `global.json`）**：首次使用先 `dotnet tool restore`；
**仅限本地开发，禁止在 CI/CD 中使用**（第三方工具，需 restore + roll-forward，不适合流水线）。

```bash
dotnet r build / test / run / web / desktop / clean / format / ci
dotnet r build -- -c Release                   # `--` 之后的参数透传给命令
```

**测试栈**：4 个项目 —— `*.Core.Tests` / `*.Application.Tests` / `*.Infrastructure.Tests`（xunit.v3 4.0.1）
+ `SeatFlow.Presentation.Tests`（Headless UI，**锁定 xunit.v3 3.2.2**，4.0.x 会 `MissingMethodException`；
用 `Avalonia.Skia` 真实绘制，**锁定 `SkiaSharp.NativeAssets.Linux 4.153.1`**，否则 Linux 上崩溃）。
其余项目 `<ImplicitUsings>enable</ImplicitUsings>`，项目级 using 在 `Usings.cs`（Application.Tests 为 `Using.cs`）。
**没有** `Directory.Build.props` / `Directory.Packages.props` —— 包版本直接写在各自 `.csproj`。
根 `dotnet-tools.json` 含 avdt / vpk / run-script。

## 架构

- **技术栈**：.NET 10 + Avalonia **12.1.3** + CommunityToolkit.Mvvm 8.4 + **ReactiveUI.Avalonia 12.1.5**
  双范式共存：视图层保持普通 `UserControl`；新 VM 可用 `ReactiveObject`/`[Reactive]`/`ReactiveCommand`/`WhenAnyValue`，
  存量 VM 仍以 CTK 源生成器为主。决策见 `docs/adr/ADR-014`。解决方案文件为 `SeatFlow.slnx`（XML 格式）。
- **分层（自底向上）**：
  - **Core** — 领域实体、策略接口与 7 个内置实现、领域服务、`SeatingWorkspace`、数据提供者接口。
  - **Infrastructure** — CSV/XLSX/JSON 读写（EPPlus 8）、导出器（Excel/CSV/PDF/图片）、三种布局构建器、仓储、迁移系统。
  - **Application** — `IApplicationFacade`（UI 唯一入口）、`StrategyExecutionPipeline`、撤销/重做命令、DI 注册。
  - **Presentation.Avalonia** — 共享 UI 类库（`net10.0;net10.0-browser`）。
  - 依赖链：`Presentation.Avalonia` → `Application` → `Core`；`Infrastructure` → `Core`。
- **日志**：Serilog 4 + `ILogger<T>`，文件 sink，`AppSettings.CategoryOverrides` 支持分模块等级；详见 `docs/LOGGING.md`。
- **双壳**：`SeatFlow.Desktop`（EXE，AssemblyName=`SeatFlow`）与 `SeatFlow.Browser`（WASM 静态站）。
  存储经 `ILocalDataStore`（桌面=文件系统 / WASM=IndexedDB）；PDF/图片导出、自动更新、Watchdog、单实例仅桌面；
  Web 对话框为 overlay。浏览器外壳 `MainView : UserControl`（不能构造 `Window`），JSON 需反射序列化开关，
  CJK 由嵌入 Noto Sans SC 回退，语言在 `Program.Main` 于 Avalonia 启动前异步预加载。
  **WASM 运行时语言切换**需 `UseSystemResourceKeys=false` + `main.js` 的 `loadAllSatelliteResources: true`（详见 `docs/WebDeployment.md`）。
- **DI**：`AddSeatFlowApplication(snapshotBasePath)` 注册应用层服务；壳层再注册
  `INavigationService`/`IFileService`/`IDialogService`/`IDialogGate`/`IShellLayoutService`/`MainWindow`/`MainShellViewModel`/全部页面 VM；
  `services.AddGuideSeedTargets()` 注册 5 个 `IGuideSeedTarget`。`App` 构造参数化（DI + 桌面启动参数），无静态握手。
- **导航**：7 页 `PageKey`（默认入口 `SeatingArrangement`；`Home` 已移除，`FreeformManagement` 并入 `VenueConfiguration`）：
  排座工作台 / 人员管理 / 会场与布局 / 策略配置 / 历史快照 / 设置 / 关于。
  `ViewLocator` 按命名约定解析 VM→View，并按 VM 实例弱引用缓存（切页不重建 XAML）。
- **启动顺序**：`StartupGuard` 校验环境 → `App.Initialize()` 先设语言再加载 XAML →
  `OnFrameworkInitializationCompleted` 解析壳与首页 → `SetTopLevel` → Watchdog（仅桌面）→
  `ChineseInputNormalizer` → `SafeInitializeAsync()`（自动导入 → 首次启动 → 恢复设置）。
  对话框与日志自 M0 起全部构造注入（无 `ViewModelBase.Dialog` 静态状态）。

### 策略管道

fill-in-order 模型：独立策略按 Priority 降序执行（先到先得，无覆盖语义）；依赖策略在 `RandomFillStrategy` 的分配循环内执行。

| 顺序 | 策略 | Priority | 类型 | 作用 |
|---|---|---|---|---|
| 1 | `FixedSeatStrategy` | 100 | 独立 | 锁定固定座位（`IsFixed=true`），后续 `GetEmptySeats()` 自动排除 |
| 2 | `FrontRowRotationStrategy` | 50 | 独立 | 填充前排座位，Fisher-Yates 随机分布 |
| — | `DeskMateStrategy` | 50（上下文） | 依赖 | 同桌组协调入座（同行+邻列+同 SeatsPerDesk）；可驱逐 RandomFill 已分配学生，不动先前策略/固定座位；不足时部分入座并警告 |
| — | `GenderRestrictedSeatStrategy` | 45（上下文） | 依赖 | 性别限制座位；不符则重定向到匹配空座，无可用则重掷，耗尽后带警告强制 |
| — | `NoRepeatDeskMateStrategy` | 40（上下文） | 依赖 | 检查相邻已占用座位的历史同桌重复；重复则重掷，耗尽后带警告强制 |
| 3 | `RandomFillStrategy` | 1 | 独立+宿主 | 填剩余座位并宿主依赖策略；约束学生（同桌组）优先以减少重掷 |
| 4 | `DefragStrategy` | 0 | 独立 | “扫地僧”：从后排搬无约束学生补前排空缺（可跨列），默认禁用；警告可能使先前策略结果失效 |

**声明式策略配置**：只改 `src/SeatFlow.Core/Strategies/Manifests/*.json`，不动 C#。
顶层字段：`visible`（是否参与管线与 UI）、`isIndependent`（false=依赖策略）、`manifestVersion`、
`parameters[]`（`NumberInput`/`TextInput`/`ToggleSwitch`/`Dropdown` + `label` 内联 i18n + `defaultValue`/最小最大）、
`codeBlocks[]`（`dataType`: Student/Venue/Both；`displayMode`: Table/ValuePair；`showSeatPosition`、
`showStudentPicker`/`showVenuePicker`、`studentPickerCount`、`seatsPerDeskFromVenue`、
`preventDuplicateInRow`/`preventDuplicateAcrossRows`、`loadTrigger`: Both/Any）。
运行期消息用 `workspace.LogWarning/LogError(strategyId, displayName, messageKey, args)`，
`messageKey` 对应 manifest `messages` 的内联 i18n 字典，结果收集在 `SeatingWorkspace.Messages`。
配置行加载按「有值的选择器才参与匹配」：`dataType: Both` 仅选数据集即加载（会场为通配），再选会场收窄；
学生选择经 `_pendingSelections` 延迟到名单加载后回填。详见 `docs/adr/ADR-006`。

### 页面生命周期与横切服务

- `IPageLifecycle`：`OnEnterAsync(ct)` / `OnLeaveAsync()` / `IsDirty` / `InitializationTask`；
  **构造器禁止 fire-and-forget 加载**；`InitializationTask` 进入流程结束即置位、离开换新；
  数据加载成功标志仅在成功时置位（取消/失败下次进入自动重试）。
- `DirtyTracker`：统一 JSON 快照脏检查，勿再手写。
- `IDialogGate`：并发对话框门，替代 `_dialogLock + Task.Delay(150)`。
- `IShellLayoutService`：≤900px 全局紧凑断点（内联面板 ↔ 抽屉，`SideDrawerState` 复用）。
- `IGuideSeedTarget`：引导演示数据注入契约（`SeedGuideData`/`ClearGuideData`，5 页自行实现）。
- `WatchdogService`：45s UI 挂起检测（仅桌面），UI 线程须定期 `Ping()`（`App.axaml.cs` 的 DispatcherTimer）。
- `SeatingCanvas`：自绘 `Control.Render` 画布（Grid/Polar/Freeform 统一几何命中；拖拽/交换/平移/缩放/方向键虚拟焦点内置）。

## 关键模式与约定

- **MVVM**：CTK 源生成器（`[ObservableProperty]` / `[RelayCommand]` / `[NotifyPropertyChangedFor]`）+ ReactiveUI（新代码优先）。
  所有 VM 继承 `ViewModelBase`；`IDialogService` 与 logger 构造注入，无对话框场景传 `NullDialogService.Instance`。

```csharp
protected Task<bool> SafeExecuteAsync(Func<Task> action, string? errorTitle = null)
protected Task<bool> SafeExecuteAsync(Func<CancellationToken, Task> action, TimeSpan timeout, string? errorTitle = null)
public virtual Task<bool> CanLeaveAsync()   // NavigationService 离开页面前调用
```

  长任务（导入/导出）优先用超时重载，超时值需远小于 Watchdog 45s 阈值。
- **主题与令牌**：方向 B「方格纸」。颜色/间距/圆角/字阶只用 `Resources/Tokens/` 令牌
  （`{DynamicResource Sf*Brush}` / `{StaticResource SfSpace*/SfRadius*}`），**禁止硬编码色值**；
  明暗双主题同等维护；组件样式在 `Styles/Components.axaml`（`sf-*` 类）。详见 `docs/presentation/Design_Spec.md`。
- **XAML**：根元素必须 `x:DataType`（全量编译绑定，`ReflectionBinding` 实际使用为 0）；
  图标 `<fic:FluentIcon Icon="{x:Static ficEnum:Icon.X}" FontSize="18"/>`；
  转换器在 `BoolConverters` / `ValueConverters`；**DockPanel 的 `Dock` 子元素必须在填充子元素之前**。
- **行为**：`ChineseInputNormalizer`（全角→半角）；`FileDropHandler`（全局文件拖放，Tunnel 事件路由到 `IFileDropHandler`）。
  拖放路由：SeatingArrangement `.seatsets`（`SeatSetsImportHelper`）、MemberManagement `.csv/.xlsx/.json`、
  VenueConfiguration `.csv/.json`、Settings `.seatsets`。新页面支持拖放 = 实现 `IFileDropHandler` 三个成员。
  详见 `docs/presentation/DragDrop.md`。
- **新增页面**：`PageKey` 加枚举 → 建 `XxxViewModel` / `XxxView.axaml`（设 `x:DataType`）→ `Program.cs` 注册 →
  `MainView.axaml` 侧栏加入口（桌面 `MainWindow` 复用同一外壳）。
- **对话框窗口**：`DialogWindow` / `InputWindow` 按钮用 `Content="{x:Static}"` 属性语法；
  `Window` 子类中 `Resources` 指 `Window.Resources`，须用 `Lang.Resources.Xxx` 全名。

### i18n

`.resx` 三文件：`Resources.resx`（zh-CN 中性）/ `Resources.en-US.resx` / `Resources.Designer.cs`（手工维护）。
键名 `{Page}_{Element}` PascalCase，格式串用 `{0}`。XAML 仅属性语法 `{x:Static lang:Resources.Key}`；
C# 用 `Resources.Key`；内置策略等字典文案走 `LocalizeHelper.Resolve(dict)`（内联 i18n）。
**新增/修改 key 一律用脚本**（自动同步三文件，备份在 `Lang/.backup/`）：

```bash
python3 scripts/i18n.py list | check | sync
python3 scripts/i18n.py add KEY --zh "中" --en "EN" | modify | rename OLD NEW | delete KEY
python3 scripts/i18n.py export -o t.csv | import t.csv --dry-run|--force
```

### 脚本工具

完整参考 `scripts/ToolsCollection.md`。`scripts/version.py` 管理 15+ 处版本一致性
（`show`/`check`/`bump-app`/`bump-file`/`bump-strategy`/`bump-onboarding`/`sync`；`bump-file` 自动同步 Model 类）。
`scripts/build/publish.*` 多平台发布，`scripts/build/clean.*` 清理，`scripts/release/release.py` 发布编排
（读根目录 `RELEASE.md` 作为 Release body）；`scripts/ci/` 发布流水线辅助（`fetch_previous.sh` 预发布
delta 基础、`sync_velopack_history.py` 稳定版从 OSS 同步打包历史、`stage_velopack_artifacts.sh` 暂存
本次产物），详见 `.github/docs/RELEASE_FLOW.md`；`scripts/ui-inspect/` 无头 UI 查看/交互/性能采样工具链。
脚本测试：`cd scripts && python3 -m pytest tests/ -v`。
App 版本唯一来源是根目录 `version.json`（`commitId`/`buildDate` 由构建脚本生成，勿手改）。

## 数据、文件与迁移

**数据目录**（OS 标准；`AppSettings.DataDirectory` 可覆盖）：
Windows `%APPDATA%\SeatFlow\` / Linux `~/.local/share/SeatFlow/` / macOS `~/Library/Application Support/SeatFlow/`。
结构：`AppSettings.json`、`Logs/`、`Venues/*.venue.json`、`Rosters/*.roster.json`、
`Assignments/{venueId}/{yyyyMMdd}/*.json`、`StrategyConfig/{strategyId}[/*].config.json`。

**文件版本**（`src/SeatFlow.Infrastructure/Migration/file_versions.json`，运行时 `FileVersionInfo.GetCurrentVersion`）：

| 类型 | 版本 | 包装类 |
|---|---|---|
| Venue | 1.1 | `VenueFile` |
| Roster | 1.1 | `RosterFile` |
| Snapshot | 1.0 | `SeatingSnapshot` |
| VenueInfo | 1.0 | `VenueSnapshotInfo` |
| AppSettings | 1.1 | `AppSettings` |
| StrategyConfig / StrategyDatasetConfig | 1.0 | 同名 |

**迁移规则**：加载时读 `JsonNode` → `FileMigrationService.Migrate`（仅向前）→ 反序列化。
新增字段有合理默认值就**不要**写迁移器；仅当「不转换会出错/丢信息」才写（格式重排、语义变化、重命名、结构重组）。
新增迁移器：`Migration/Migrators/{FileType}Migrators.cs` 内嵌 `IFileMigrator` 类 → `ServiceCollectionExtensions` 注册 →
`file_versions.json` 升版 → 同步 Model 默认值 → 加测试（可用 `scripts/version.py bump-file` 自动同步）。

**JSON 约定**：camelCase；`ClassroomLayoutDefinition` 同时写 `layoutType`（数字）与 `layoutTypeString`（迁移器读字符串）；
座位多态用 `SeatJsonConverter` 的 `Type` 判别字段；`VenueFile`/`RosterFile.ContentHash` 保存时计算 SHA256
（学生数据哈希排除 `importedAt`/`originalFileName`）。Grid 座位按**行主序**生成（RandomFill 逐行填充的前提）。

**快照**：创建时把完整布局嵌入 `Metadata["venueLayout"]`（预览自包含，会场被删/改不破坏旧快照）；
`venueHash` 对比现行会场做完整性提示（会场删除=红、布局变更=黄、数据变更=黄并高亮）；回滚前按完整性弹窗恢复/导入会场。
`AppSettings.MaxSnapshotsPerVenue`（默认 30，0=不限）超限自动删最旧。

**会场**：编辑保存时按位置复用座位 ID（`(Row,Column)` / `(Ring,Angle)` 映射），避免快照 `SeatAssignments` 失效。

**名单数据集**：点选即加载（无 Load 按钮）；脏检查 = `DirtyTracker` + `IsNewStudentDirty`；
切换数据集走三键对话框（保存/放弃/取消），取消经 `_suppressDatasetLoad` 回退；导入数据无 ID 时保存走另存（`RenameSaveAsync`）。

**引导系统**：`Data/onboarding_config.json`（v3.4，24 步启动引导 + 页面引导机制）；
`OnboardingService` 只经 `IGuideSeedTarget` / `IPageLifecycle` 接口交互，改步骤只需改 JSON + resx。
详见 `docs/ONBOARDING_GUIDE.md`、`docs/adr/ADR-008`。

**页面开关**：`Data/page_navigation.json` 控制页面启用；禁用页在 `MainShellViewModel` 加属性，
侧栏用 `Opacity`（非 `IsEnabled`，否则 ToolTip 失效）+ `ToolTip.Tip` 绑定，提示键 `Nav_{Page}Disabled`。

**已知陷阱**：
- `VenueConfiguration.NewVenue()` 必须先取消在途 `SelectVenueAsync`（`_selectVenueCts`），否则异步加载会覆盖重置后的状态。
- About 页版本 = `about.json` 版本 + `GitCommit.Hash`（MSBuild 构建时生成，不入库）。
- 确定性构建：`Deterministic=true` + `PathMap`，同源码产出同哈希。

## 文档

- **本文件（`AGENTS.md`）是 AI 指南唯一来源**；根 `CLAUDE.md` 仅为指针。修改指南只改本文件。
- `docs/INDEX.md` — 文档地图与联动规则，**修改文档前先读**。
- 其他关键文档：`ARCHITECTURE.md`、`docs/Phases.md`、`CONTRIBUTING.md`、`CHANGELOG.md`、
  `docs/UI_REFACTOR.md`（UI 重构记录）、`docs/presentation/Design_Spec.md`、`docs/presentation/DragDrop.md`、
  `docs/presentation/Fluent_Icons.md`、`docs/WebDeployment.md`、`docs/ONBOARDING_GUIDE.md`、
  `docs/StrategyDataResilience.md`、`docs/adr/`（ADR-001 ~ ADR-014）。
