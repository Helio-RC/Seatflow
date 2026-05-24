# A_Pair 项目参考手册

> 本文档是项目级 AI 配置 [CLAUDE.md](../CLAUDE.md) 的可读版本，供开发者快速查阅项目约定和技术细节。

## 开发环境

在执行需要 GUI 的操作前，先确认当前是否处于无头环境（检查 `DISPLAY` / `WAYLAND_DISPLAY` 等环境变量），以及是否安装了 .NET 10 SDK。avdt（Avalonia DevTools）仅在桌面环境下可用。

## 构建与测试

```bash
dotnet build                    # 构建全部 9 个项目（使用 .slnx，需要 .NET 10 SDK）
dotnet test                     # 运行所有测试（xUnit v3, Microsoft.Testing.Platform）
dotnet test --filter "FullyQualifiedName~TestName"  # 运行单个测试
dotnet run --project A_Pair.Presentation.Avalonia   # 启动桌面应用
```

**测试栈**: xUnit v3 + FluentAssertions + NSubstitute。测试项目分布在 `*.Core.Tests`、`*.Application.Tests`、`*.Infrastructure.Tests`。每个项目启用了 `<ImplicitUsings>enable`，项目级全局 using 在 `Usings.cs`（Application.Tests 中为 `Using.cs`）。

**包管理**: 无 `Directory.Build.props` 或 `Directory.Packages.props`，版本在每个 `.csproj` 中直接管理。

**dotnet tools**: `avaloniaui.developertools` (avdt) 已安装在仓库根目录的 `dotnet-tools.json` 中。使用前先确认当前环境是否有桌面显示支持，若无头则跳过。构建时无需此工具。

## 架构

A_Pair 是基于 .NET 10 + Avalonia UI 12 (MVVM) + CommunityToolkit.Mvvm 8.4 的跨平台桌面座位安排系统。解决方案文件为 `A_Pair.slnx`（新的 XML 格式）。

**分层（自下而上）**:
- **Core** — 领域实体（`Student`、`Seat`、`ClassroomLayoutDefinition`、`SeatingWorkspace`、`SeatingPlan`）、策略接口（`ISeatingStrategy`）+ 四种内置实现、领域服务（`ObstacleProcessor`、`SeatGeometryHelper`、`StrategyManifestProvider`）、数据提供者接口（`IStudentProvider`、`IVenueRepository` 等）
- **Contracts** — 跨层接口（`IPluginSeatingStrategy`）
- **Infrastructure** — 文件 I/O（`CsvStudentProvider`、`XlsxStudentProvider`、`JsonStudentProvider`、以及注册为主 `IStudentProvider` 的组合 `CompositeStudentProvider`）、导出器（`ExcelSeatingExporter`、`CsvSeatingExporter`、`PdfSeatingExporter`、`ImageSeatingExporter`）、布局构建器（`GridLayoutBuilder`、`PolarLayoutBuilder`、`FreeformLayoutBuilder`）、仓储（`JsonVenueRepository`、`JsonAppSettingsRepository`、`StrategyConfigFileRepository`、`SeatingSnapshotRepository`、`JsonStudentDatasetRepository`）、写入器（`JsonStudentWriter`、`CsvStudentWriter`、`XlsxStudentWriter`）、序列化
- **Application** — `IApplicationFacade`（UI 唯一入口）、`StrategyExecutionPipeline`、命令模式（`IUndoableCommand` / `CommandHistory`）、插件管理器（`PluginManager`、`PluginLoadContext`）、脚本适配器（Lua/C#）、DI 注册
- **Plugins.Sdk** — 供外部插件作者使用的轻量程序集
- **Presentation.Avalonia** — Avalonia 12 桌面应用，MVVM + CommunityToolkit.Mvvm

**依赖注入**: Application 层的 `ServiceCollectionExtensions.AddA_PairApplication(snapshotBasePath, pluginsPath)` 注册所有服务。`Program.cs` 中 UI 层调用此方法后，再注册自己的单例：`INavigationService`、`IFileService`、`IDialogService`、`MainWindow`、`MainShellViewModel` 以及所有页面 ViewModel。

**导航**: `INavigationService` + `MainShellViewModel` 通过 `PageKey` 枚举管理 10 个页面（`Home`、`DataManagement`、`VenueConfiguration`、`FreeformManagement`、`StrategyConfiguration`、`SeatingArrangement`、`SnapshotHistory`、`PluginManagement`、`Settings`、`About`）。`ViewLocator` 按命名约定自动解析 `XXXViewModel` → `XXXView`。

**依赖链**: `Presentation.Avalonia` → `Application` → (`Core`, `Contracts`, `Infrastructure`)。`Plugins.Sdk` 仅供外部插件引用。

**项目配置**: Avalonia 项目的 `AvaloniaUseCompiledBindingsByDefault` 为 `true`，所有绑定默认编译，除非显式退出。

**应用启动顺序** (`App.axaml.cs` `OnFrameworkInitializationCompleted`):
1. 从 DI 解析 `MainShellViewModel` 和 `MainWindow`，绑定 DataContext
2. 调用 `IFileService.SetTopLevel()` 和 `IDialogService.SetTopLevel()`，传入 MainWindow
3. 初始化 `ViewModelBase.Dialog`（静态）和 `ViewModelBase` 的 Logger
4. 启动 `WatchdogService`（3 秒 DispatcherTimer ping，防止 UI 冻结阻塞退出）
5. 挂载 `ChineseInputNormalizer` 行为（全角数字/符号 → 半角）
6. 通过 `RestoreSettingsAsync()` 恢复已保存的设置

## 关键模式

### CommunityToolkit.Mvvm 源代码生成器
- 私有字段上的 `[ObservableProperty]` → 生成公共属性 + `On<PropertyName>Changed` 分部方法钩子
- 方法上的 `[RelayCommand]` → 生成 `ICommand` 属性
- `[NotifyPropertyChangedFor(nameof(OtherProp))]` → 触发依赖属性变更通知
- 所有 ViewModel 继承 `ViewModelBase`（扩展 `ObservableObject`）

### ViewModelBase.SafeExecuteAsync

两个重载：

```csharp
// 简单版：try-catch，自动弹错误对话框
protected async Task<bool> SafeExecuteAsync(Func<Task> action, string errorTitle = "操作失败")

// 超时版：通过 CancellationTokenSource 自动取消，超时弹提示
protected async Task<bool> SafeExecuteAsync(Func<CancellationToken, Task> action, TimeSpan timeout, string errorTitle = "操作失败")
```

超时重载适合长时间运行的导出/导入操作。超时应远小于 WatchdogService 阈值（45s）。

`ViewModelBase` 使用静态 `IDialogService`，`App.axaml.cs` 必须在任何 ViewModel 使用 `SafeExecuteAsync` 之前调用 `ViewModelBase.InitializeDialogService(dialog)`。

### ViewModelBase.CanLeaveAsync
```csharp
public virtual Task<bool> CanLeaveAsync()
```
导航离开前由 `NavigationService` 调用。重写以提示用户未保存的更改。

### 主题与自定义资源
- **主题字典**: App.axaml 通过 `ResourceDictionary.ThemeDictionaries` 定义 Light/Dark 变体
- **画刷**: 通过 `DynamicResource` 引用主题色，始终使用画刷资源（如 `{StaticResource SuccessBrush}`），不硬编码颜色
- **样式引用**: FluentTheme 之后引入 `Typography.axaml`、`Spacing.axaml`、`Colors.axaml`
- **字体**: `Inter,Microsoft YaHei UI,PingFang SC,Noto Sans CJK SC,WenQuanYi Micro Hei,sans-serif`
- **阴影**: `CardShadowNone`、`CardShadowLarge`、`CardShadowSmall`

### UI 服务 (`A_Pair.Presentation.Avalonia/Services/`)
- **INavigationService** — 页面切换。`NavigateTo()` 同步，`NavigateToAsync()` 先执行 `CanLeaveAsync()`
- **IDialogService** — 错误/信息对话框，使用前需 `SetTopLevel(TopLevel)`
- **IFileService** — 文件打开/保存选择器，同样需要 `SetTopLevel()`
- **WatchdogService** — 后台轮询检测 UI 线程卡死，默认 45s 超时

### 实用窗口
- `InputWindow` — 单行文本输入模态框
- `DialogWindow` — 通用模态内容容器

### 行为 (`A_Pair.Presentation.Avalonia/Behaviors/`)
- `CanvasZoomPan` — Canvas 缩放平移
- `ZoomOnScroll` — Ctrl+滚轮缩放
- `ChineseInputNormalizer` — 全角数字/符号转半角

### 添加新页面
1. 在 `INavigationService.cs` 的 `PageKey` 枚举中添加新值
2. 创建 `ViewModels/NewThingViewModel.cs`（继承 `ViewModelBase`）
3. 创建 `Views/NewThingView.axaml` + `.axaml.cs`（设置 `x:DataType="vm:NewThingViewModel"`）
4. 在 `Program.cs` 中注册：`services.AddSingleton<NewThingViewModel>()`
5. 在 `MainWindow.axaml` 侧边栏添加导航按钮

### Axaml 绑定
- 始终在根元素上使用 `x:DataType` 启用编译绑定
- 图标：`<fic:FluentIcon Icon="{x:Static ficEnum:Icon.{Name}}" FontSize="18"/>`
- 转换器：`BoolConverters.cs`、`ValueConverters.cs`

### 侧边栏
- 宽度：展开 140px / 折叠 64px（由 `MainShellViewModel.SidebarWidth` 控制）
- 窗口宽度 < 750px 时自动折叠
- `MainShellViewModel.ToggleSidebar()` 手动切换

## 文件版本与迁移

所有持久化 JSON 文件携带 `version` 字段，加载时通过 `FileMigrationService` 自动向前迁移。版本号定义在 `A_Pair.Infrastructure/Migration/file_versions.json`（嵌入资源，随程序编译）。`FileVersionInfo.GetCurrentVersion(fileType)` 在运行时读取最新版本。

| 文件类型 | 版本 | 存储位置 | 包装类 |
|---|---|---|---|
| Venue | `1.1` | `{data}/Venues/*.venue.json` | `VenueFile` |
| Roster | `1.0` | `{data}/Rosters/*.roster.json` | `RosterFile` |
| Snapshot | `1.0` | `{data}/Assignments/{venueId}/{date}/*.json` | `SeatingSnapshot` |
| VenueInfo | `1.0` | `{data}/Assignments/{venueId}/_venue.json` | `VenueSnapshotInfo` |
| AppSettings | `1.0` | `{data}/AppSettings.json` | `AppSettings` |
| StrategyConfig | `1.0` | `{data}/StrategyConfig/*.config.json` | `StrategyConfig` |

### 迁移管线

加载时，各仓储将文件读取为 `JsonNode`，调用 `FileMigrationService.Migrate(fileType, node, fileVersion, targetVersion)`，再反序列化。迁移仅支持向前，不支持版本回退。服务查找注册的 `IFileMigrator` 实现，按 `FromVersion`→`ToVersion` 匹配后链式执行。

### 添加迁移的步骤

1. 在 `Migration/Migrators/{FileType}Migrators.cs` 的容器类中添加嵌套类：
   ```csharp
   public static class VenueMigrators
   {
       public sealed class Step_1_0_to_1_1 : IFileMigrator
       {
           public string FileType => "venue";
           public string FromVersion => "1.0";
           public string ToVersion => "1.1";
           public JsonNode Migrate(JsonNode root) { ... }
       }
   }
   ```
2. 在 `ServiceCollectionExtensions.cs` 中注册：`services.AddSingleton<IFileMigrator, VenueMigrators.Step_1_0_to_1_1>()`
3. 在 `file_versions.json` 中提升版本号
4. 在 `Core/Models/` 对应模型类中更新默认 `Version` 属性
5. 在 `Infrastructure.Tests/Migration/{FileType}MigratorsTests.cs` 添加覆盖测试

### 已有迁移器

- `VenueMigrators.Step_1_0_to_1_1` — 将 Grid 布局座位从列主序重排为行主序（按 `Row` → `Column` 排序）

### JSON 字段约定

- 序列化使用 `JsonNamingPolicy.CamelCase`，JSON 中所有字段为小写驼峰（`row`、`column`、`layoutTypeString`）
- `ClassroomLayoutDefinition.LayoutType` 同时序列化为数字（`layoutType`: 0=Grid, 1=Polar, 2=Freeform）和字符串（`layoutTypeString`: "Grid"/"Polar"/"Freeform"）— 迁移器中优先使用 `layoutTypeString`
- `Seat` 多态序列化通过 `SeatJsonConverter`，写入 `Type`（大写）鉴别器字段；`type`（小写）为 `SeatType` 枚举的独立整数字段
- `SeatingSnapshot.Version` 和 `VenueSnapshotInfo.Version` 为本次新增字段，旧快照文件默认回退为 `"1.0"`

### Grid 座位排序

`GridLayoutBuilder.BuildGrid` 以**行主序**创建座位（外层行，内层列），确保 `RandomFillStrategy` 从左到右、从上到下一行行填充。对使用 `ColumnRowCounts` 的不规则网格，用 `maxRows = ColumnRowCounts.Max()` 并在每列检查 `r <= rowsForCol`。

## 项目文档

- [INDEX.md](INDEX.md) — 文档导航地图（修改文档前先查阅）
- [ARCHITECTURE.md](../ARCHITECTURE.md) — 项目目标与架构设计
- [Phases.md](../Phases.md) — 实现阶段与详细规划
- [README.md](../README.md) — 项目概览
- [CONTRIBUTING.md](../CONTRIBUTING.md) — 开发参与指南
- [docs/adr/](adr/) — 架构决策记录
- [A_Pair.Presentation.Avalonia/docs/Design_Spec.md](../A_Pair.Presentation.Avalonia/docs/Design_Spec.md) — UI 设计规范
- [A_Pair.Presentation.Avalonia/docs/Fluent_Icons.md](../A_Pair.Presentation.Avalonia/docs/Fluent_Icons.md) — 图标参考
- [A_Pair.Plugins.Sdk/docs/README.md](../A_Pair.Plugins.Sdk/docs/README.md) — 插件开发 SDK
