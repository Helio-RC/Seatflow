# 参与开发

## 环境搭建

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows / macOS / Linux
- 推荐 IDE：Rider、VS Code + C# 扩展、Visual Studio 2022+

```bash
git clone <repo-url> && cd A_Pair
dotnet restore
```

## 常用命令

| 命令 | 说明 |
|------|------|
| `dotnet restore` | 还原 NuGet 依赖 |
| `dotnet build` | 构建全部 9 个项目 |
| `dotnet test` | 运行所有测试 |
| `dotnet test --filter "FullyQualifiedName~TestName"` | 运行单个测试 |
| `dotnet run --project A_Pair.Presentation.Avalonia` | 启动桌面应用 |

测试栈：xUnit v3 + FluentAssertions + NSubstitute。测试项目分布在 `*.Core.Tests`、`*.Application.Tests`、`*.Infrastructure.Tests`。

## 项目结构

```
A_Pair.slnx
├── A_Pair.Core/                  → 领域核心：实体、策略接口、领域服务
├── A_Pair.Contracts/             → 共享契约：跨层接口（插件契约等）
├── A_Pair.Application/           → 应用层：外观、策略管道、命令历史、插件管理、DI
├── A_Pair.Infrastructure/        → 基础设施：数据提供者、导出器、布局构建器、仓储
├── A_Pair.Plugins.Sdk/           → 插件 SDK（供外部插件引用）
├── A_Pair.Presentation.Avalonia/ → Avalonia 12 桌面应用
├── A_Pair.Core.Tests/
├── A_Pair.Application.Tests/
└── A_Pair.Infrastructure.Tests/
```

**分层依赖**：`Presentation.Avalonia` → `Application` → (`Core`, `Contracts`, `Infrastructure`)

包版本在每个 `.csproj` 中直接管理，无 `Directory.Build.props` 或 `Directory.Packages.props`。

## 架构要点

参见 [ARCHITECTURE.md](ARCHITECTURE.md)（架构总览）、[Phases.md](Phases.md)（阶段划分）和 [docs/adr/](docs/adr/)（架构决策记录）。

关键模式：
- **外观模式** — `IApplicationFacade` 是 UI 层唯一入口
- **策略模式** — `ISeatingStrategy` 按优先级管道执行
- **命令模式** — `IUndoableCommand` + `CommandHistory` 撤销/重做
- **插件隔离** — `AssemblyLoadContext` 独立加载外部 DLL

## 编码约定

- 面向接口编程，通过 DI 容器管理生命周期
- ViewModel 继承 `ViewModelBase`，使用 `[ObservableProperty]` / `[RelayCommand]` 源代码生成器
- Axaml 绑定使用 `x:DataType` 启用编译绑定（项目已开启 `AvaloniaUseCompiledBindingsByDefault`）
- 异步操作通过 `SafeExecuteAsync` 包装，自动处理异常和超时
- 无头环境中无法使用 Avalonia DevTools

## 国际化 (i18n)

使用 .NET `.resx` 资源文件，位于 `A_Pair.Presentation.Avalonia/Lang/`：

- `Resources.resx` — 中性语言 (zh-CN)，~570 键
- `Resources.en-US.resx` — 英文卫星资源
- `Resources.Designer.cs` — 手动维护的强类型访问器类

**XAML 用法**（仅属性语法，元素内容不解析）：
```xml
<TextBlock Text="{x:Static lang:Resources.Settings_Title}" />
<Button Content="{x:Static lang:Resources.Common_OK}" />
```

**C# 用法**：
```csharp
StatusMessage = Resources.Settings_Saved;
StatusMessage = string.Format(Resources.Snapshot_VenuesLoadedFmt, count);
```

**键命名**: `{Page}_{Element}`，如 `Settings_Title`、`Nav_Home`、`Common_OK`。格式化字符串用 `{0}` 占位符。

**新增语言**: 在 `Lang/` 下创建 `Resources.xx-XX.resx`，翻译所有键即可，无需代码变更。

**语言切换**: 在 `AppSettings.Language` 中设置语言代码（如 `en-US`），重启后生效。

## 添加新页面

1. 在 `INavigationService.cs` 的 `PageKey` 枚举中添加新值
2. 创建 `ViewModels/XXXViewModel.cs`（继承 `ViewModelBase`）
3. 创建 `Views/XXXView.axaml` + `.axaml.cs`（设置 `x:DataType="vm:XXXViewModel"`）
4. 在 `Program.cs` 中注册 `services.AddSingleton<XXXViewModel>()`
5. 在 `MainWindow.axaml` 侧边栏添加导航按钮

`ViewLocator` 通过命名约定自动解析 `XXXViewModel` → `XXXView`。

## 文件版本管理

所有持久化 JSON 文件携带 `version` 字段，加载时通过 `FileMigrationService` 自动向前迁移。版本号定义在 `A_Pair.Infrastructure/Migration/file_versions.json`（嵌入资源）。

### 添加新版本迁移

1. 在 `Migration/Migrators/{FileType}Migrators.cs` 的容器类中新增嵌套类，实现 `IFileMigrator`：
   ```csharp
   public static class VenueMigrators
   {
       public sealed class Step_1_1_to_1_2 : IFileMigrator
       {
           public string FileType => "venue";
           public string FromVersion => "1.1";
           public string ToVersion => "1.2";
           public JsonNode Migrate(JsonNode root) { /* 迁移逻辑 */ }
       }
   }
   ```
2. 在 `ServiceCollectionExtensions.cs` 注册：`services.AddSingleton<IFileMigrator, VenueMigrators.Step_1_1_to_1_2>()`
3. 在 `file_versions.json` 中提升版本号
4. 在 `Core/Models/` 对应模型类中更新默认 `Version` 属性
5. 在 `Infrastructure.Tests/Migration/{FileType}MigratorsTests.cs` 添加覆盖测试

### JSON 字段约定

- 序列化使用 `JsonNamingPolicy.CamelCase`，所有字段在 JSON 中为小写驼峰
- `layoutType` 为数字枚举值，`layoutTypeString` 为对应字符串 — 迁移器中优先使用 `layoutTypeString`
- `Seat` 多态序列化通过 `SeatJsonConverter` 写入 `Type`（大写）鉴别器字段

## 提交约定

- 提交前确保 `dotnet build` 和 `dotnet test` 通过
- 提交信息使用中文描述改了什么、为什么改

## AI 辅助开发

本项目使用 Claude Code 辅助开发。项目级 AI 配置位于 [CLAUDE.md](CLAUDE.md)。AI 开发者应优先阅读此文件和 `docs/adr/` 中的架构决策记录，以理解既有设计的上下文和权衡。
