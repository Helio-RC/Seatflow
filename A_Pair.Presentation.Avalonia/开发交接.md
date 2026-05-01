# A_Pair 项目 UI 开发任务交接文档

## 1. 项目概述

**A_Pair** 是一个跨平台桌面座位安排与轮换系统，使用 .NET 10、Avalonia UI、CommunityToolkit.Mvvm 构建。核心业务逻辑（领域模型、策略引擎、数据加载/导出、插件系统、配置管理）已全部实现并通过单元测试。**当前需完成 Phase 6 及后续的 UI 可视化与交互部分**，最终交付可运行的桌面应用。

## 2. 已完成的层级

### 2.1 解决方案结构
```
A_Pair.slnx
├── src/
│   ├── A_Pair.Core             # 领域核心：实体、策略接口、工作区、领域服务
│   ├── A_Pair.Contracts        # 共享契约：插件接口（IPluginSeatingStrategy）
│   ├── A_Pair.Application      # 应用层：外观Facade、管道、命令、插件管理、脚本引擎
│   ├── A_Pair.Infrastructure   # 基础设施：文件读写、导出器、布局构建器、存储库
│   ├── A_Pair.Plugins.Sdk      # 插件 SDK（空壳）
│   └── A_Pair.Presentation.Avalonia  # Avalonia UI 项目（已有窗口壳和少量控件）
└── tests/
    ├── A_Pair.Core.Tests
    ├── A_Pair.Application.Tests
    └── A_Pair.Infrastructure.Tests
```
所有核心层、应用层、基础设施层的测试均通过。

### 2.2 关键已实现的业务外观

**`IApplicationFacade`** 提供 UI 所需的所有操作：

- **数据管理**：`LoadStudentsAsync(source, ct)`、`ExportStudentsAsync(path, students, format, ct)`
- **会场管理**：`SaveVenueAsync`, `LoadVenueAsync`, `ListVenueIdsAsync`
- **策略配置**：策略已通过 DI 注册，可通过更改 `ISeatingStrategy.IsEnabled` 和 `Priority` 配置
- **座位生成**：`GenerateSeatingAsync(request, progress, ct)` 返回 `SeatingWorkspace`
- **导出座位表**：`ExportSeatingPlanAsync(workspace, path, options, ct)`
- **手动微调**：`ExecuteCommandAsync(IUndoableCommand, ct)`、`UndoAsync`、`RedoAsync`
- **快照管理**：`GetSnapshotsAsync(venueId, ct)`、`RollbackToSnapshotAsync(snapshotId, ct)`
- **应用设置**：`LoadAppSettingsAsync`, `SaveAppSettingsAsync`

**`SeatingWorkspace`** 提供：

- `Students` 只读学生列表
- `GetEmptySeats()`, `FindSeats(predicate)`
- `TryAssignSeat(seatId, studentId, out error)` 
- `BuildSeatingPlan()` 生成 `SeatingPlan`（`Dictionary<string,string>`）

## 3. UI 实现任务

需要完成 `A_Pair.Presentation.Avalonia` 项目中的所有页面和交互逻辑，使用 **CommunityToolkit.Mvvm** 作为 MVVM 框架。

### 3.1 当前 Avalonia 项目现状

已有文件：
- `App.axaml` / `App.axaml.cs`：应用程序入口
- `Program.cs`：Main 方法，需配置 DI、Serilog 等
- `ViewLocator.cs`：视图定位器（需改为基于命名约定的 View-ViewModel 匹配）
- `Controls/SeatCanvas.axaml` / `.cs`：空白自定义控件（需重写）
- `ViewModels/` 下仅有 `MainShellViewModel.cs`、`MainWindowViewModel.cs`、`PlaceholderViewModels.cs`、`ViewModelBase.cs`
- `Views/` 下已有 `MainWindow.axaml` 和 5 个界面文件壳（DataManagementView、VenueConfigurationView、StrategyConfigurationView、SeatingArrangementView、SnapshotHistoryView、PluginManagementView），但内容为空或极简。

**需要重写或填充所有视图与视图模型。**

### 3.2 页面清单与 ViewModel 设计

基于设计文档 `页面设计.md`，需实现以下 8 个功能页面和一个主窗口：

#### 主窗口 `MainWindowViewModel`
- 属性： `CurrentViewModel` (当前显示的页面 VM)，`StatusMessage`, `CurrentVenueName`, `TotalStudents`, `AssignedSeats`
- 命令：`NewProjectCommand`, `OpenCommand`, `SaveCommand`, `GenerateSeatingCommand`, `UndoCommand`, `RedoCommand`
- 导航：维护一个 `INavigationService`（可自实现），通过侧边栏切换页面。
- 侧边栏按钮绑定导航命令，高亮当前页。

#### 3.2.1 数据管理页 `DataManagementViewModel`
- 属性：`ObservableCollection<Student> Students`、`SelectedStudent`、`DataSourcePath`、`ValidationMessages`（字符串集合）、`Statistics`（各统计数字）
- 命令：`ImportFromXlsxCommand`, `ImportFromCsvCommand`, `ImportFromJsonCommand`, `ExportCommand`, `ValidateCommand`, `ClearCommand`
- 实现：
  - 导入时调用 `IApplicationFacade.LoadStudentsAsync` 并显示文件选择对话框
  - 导出时调用 `IApplicationFacade.ExportStudentsAsync`
  - 验证可简单遍历学生字段或调用预留的 `IDataValidator`（若已注册）

#### 3.2.2 会场配置页 `VenueConfigurationViewModel`
- 属性：`LayoutType`（枚举：Grid/Polar/Freeform）、`Rows`, `Columns`, `RadiusStep`, `Rings`, `SeatsPerRing`, `FreeformPoints`、`Obstacles` 集合、`PreviewSeats`（用于画布绑定的座位列表）
- 命令：`NewLayoutCommand`, `SaveLayoutCommand`, `LoadLayoutCommand`, `AddObstacleCommand`, `RemoveObstacleCommand`
- 实现：
  - 根据 `LayoutType` 动态切换属性面板（使用数据模板选择器）
  - 实时更新 `PreviewSeats`，调用相应的 `GridLayoutBuilder`、`PolarLayoutBuilder` 或 `FreeformLayoutBuilder`
  - 障碍物处理：添加/编辑/删除
  - 保存时调用 `IApplicationFacade.SaveVenueAsync`

#### 3.2.3 策略配置页 `StrategyConfigurationViewModel`
- 属性：`ObservableCollection<StrategyItem> Strategies`（其中 `StrategyItem` 包装 `ISeatingStrategy`，包含 `IsEnabled`, `Priority`, `Name`, `ConfigEditor` 等）
- 命令：`MoveUpCommand`, `MoveDownCommand`, `ConfigureCommand`（打开特定策略的配置面板）
- 实现：
  - 从 DI 获取所有 `ISeatingStrategy` 服务，构造 `StrategyItem` 列表
  - 排序逻辑：手动调整 `Priority` 并重新排序，或使用命令上移/下移
  - 右侧动态配置区域：依据策略的不同类型显示对应的配置控件（如 `FixedSeatStrategy` 显示座位-学生映射表格）。可使用 `ContentControl` 配合 `DataTemplateSelector`。

#### 3.2.4 座位安排页 `SeatingArrangementViewModel`（核心页面）
- 属性：
  - `SeatingWorkspace Workspace`（当前工作区）
  - `ObservableCollection<SeatViewModel> Seats`（用于画布绑定）
  - `ObservableCollection<StudentViewModel> UnassignedStudents`
  - `SeatViewModel SelectedSeat`
  - `SeatingRequest CurrentRequest`（生成参数）
  - 缩放比例、平移偏移等
- 命令：`GenerateSeatingCommand`, `ClearSeatingCommand`, `ExportCommand`, `RefreshCommand`
- 拖拽和交互：
  - 从未分配列表拖拽学生到座位，将调用 `AssignSeatCommand` 并通过 `IApplicationFacade.ExecuteCommandAsync` 执行
  - 座位间拖拽交换（需自定义 `DragDropBehavior`）
  - 鼠标悬停显示 Tooltip（学生详情）
  - 撤销/重做：直接调用 `IApplicationFacade.UndoAsync/RedoAsync`
- 重要：`SeatViewModel` 应包含 `Id`, `X`, `Y`, `OccupantName`, `IsFixed`, `IsAvailable`，以及边框样式（根据类型）。座位坐标通过 `SeatGeometryHelper` 和布局元数据计算。

#### 3.2.5 历史快照页 `SnapshotHistoryViewModel`
- 属性：`ObservableCollection<SeatingSnapshot> Snapshots`, `SelectedSnapshot`, `PreviewAssignments`（用于缩略图）
- 命令：`LoadSnapshotsCommand`, `ApplySnapshotCommand`, `SaveAsNewCommand`
- 实现：
  - 根据当前会场 ID 调用 `IApplicationFacade.GetSnapshotsAsync`
  - 应用快照调用 `RollbackToSnapshotAsync`

#### 3.2.6 插件管理页 `PluginManagementViewModel`
- 属性：`ObservableCollection<PluginInfo> Plugins`（包含名称、版本、类型、是否启用等）
- 命令：`InstallPluginCommand`, `EnablePluginCommand`, `DisablePluginCommand`, `EditScriptCommand`
- 实现：
  - 通过 `PluginManager`（已注册为单例）获取加载的插件
  - 启用/禁用修改 `IPluginSeatingStrategy.IsEnabled`
  - 脚本插件可打开简单编辑器（直接修改 `config.json` 内容）

#### 3.2.7 设置页 `SettingsViewModel`
- 属性：主题（亮色/暗色/跟随系统）、语言、数据目录路径、自动保存间隔等
- 命令：`SaveSettingsCommand`, `ResetDefaultsCommand`
- 实现：
  - 通过 `IApplicationFacade.LoadAppSettingsAsync` / `SaveAppSettingsAsync` 读写设置
  - 主题切换通过 Avalonia `ThemeVariant` 实现
  - 设置项绑定到对应的 AppSettings 属性

#### 3.2.8 关于页 `AboutViewModel`
- 属性：版本号、项目主页链接、依赖库列表、许可证信息
- 命令：无特殊命令
- 实现：
  - 显示应用名称、版本号（从 Assembly 信息读取）
  - 列出关键依赖库及版本
  - 提供项目链接（可点击打开浏览器）

### 3.3 依赖注入与启动配置

在 `Program.cs` 中需要：

1. 创建 `ServiceCollection`，调用 `AddA_PairApplication(snapshotBasePath, pluginsPath)`（已实现）
2. 注册 UI 服务：`INavigationService`、`MainWindowViewModel`、各页面 ViewModel（建议作为 Transient 或 Singleton）
3. 配置 Serilog（可选）
4. 构建 `ServiceProvider`
5. 启动 Avalonia 应用，将 `MainWindowViewModel` 设置为主窗口 DataContext

示例：
```csharp
var services = new ServiceCollection();
services.AddA_PairApplication("AppData", "Plugins");
services.AddSingleton<MainWindowViewModel>();
services.AddTransient<DataManagementViewModel>();
services.AddTransient<SettingsViewModel>();
services.AddTransient<AboutViewModel>();
// ... 其他 VM

var provider = services.BuildServiceProvider();
var mainVM = provider.GetRequiredService<MainWindowViewModel>();
// 启动窗口
```

### 3.4 CommunityToolkit.Mvvm 使用规范

- ViewModel 继承 `ObservableObject`
- 属性使用 `[ObservableProperty]` 源生成器
- 命令使用 `[RelayCommand]`
- 避免使用旧的 `ReactiveUI`，已存在的 `ViewModelBase` 可改为继承 `ObservableObject`
- `ViewLocator` 需根据命名约定（`XXXViewModel` → `XXXView`）自动查找并绑定 DataContext。

示例 `ViewModelBase` 更新：
```csharp
public partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;
}
```

### 3.5 自定义控件

- **`SeatCanvas`**：继承 `Canvas`，处理 `PointerPressed/Moved/Released` 实现拖拽、缩放平移。座位项可通过 `ItemsControl` 绑定，使用 `Canvas.Left/Top` 定位。
- **座位模板**：依据 `SeatType`（Grid/Polar/Freeform）绘制矩形或圆形，内部显示学生姓名。

### 3.6 样式与主题

- 使用 Avalonia 的 `FluentTheme`，支持亮色/暗色
- 可在 `App.axaml` 中引入主题，并定义全局样式
- 主布局可参考设计文档中的网格结构

## 4. 关键接口与数据流

所有业务逻辑都通过 `IApplicationFacade` 调用，UI 层不应直接访问仓储或领域服务。核心流程示例：

- 生成座位表：  
  创建 `SeatingRequest` → `GenerateSeatingAsync` → 返回 `SeatingWorkspace` → 转换为 `SeatViewModel` 集合 → 绑定画布。  
- 手动分配：  
  拖拽触发 → 构造 `AssignSeatCommand` → `ExecuteCommandAsync` → 刷新视图状态。  
- 导出：  
  当前工作区 + `ExportOptions` → `ExportSeatingPlanAsync`。

## 5. 测试与质量

UI 层可编写少量集成测试或使用 Avalonia 的 UI 测试框架，但本项目主要依赖已完成的后端单元测试，UI 只需确保交互正确。请务必确保所有 ViewModel 命令可正常执行，无未捕获异常。

## 6. 交付标准

- 所有 6 个页面及主窗口完整实现，可运行并执行基本操作。
- 导入/导出、座位生成、拖拽交换、撤销/重做、快照回滚功能可用。
- 使用 CommunityToolkit.Mvvm，无遗留 ReactiveUI 依赖。
- 遵循 MVVM 模式，视图代码后置尽量少。

---

请基于以上文档和现有代码库，继续完成 UI 部分的开发。如有缺少的接口或需要调整的后端代码，可酌情修改，但尽量保持核心层稳定。