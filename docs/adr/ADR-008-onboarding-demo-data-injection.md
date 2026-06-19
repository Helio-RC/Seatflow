# ADR-008：引导系统纯内存示例数据注入

## Status
Accepted

## Date
2026-06-19

## Context

A_Pair 的首次启动引导（onboarding guide）需引导用户逐步完成"导入成员→创建会场→配置策略→生成排座→查看快照"的完整工作流。引导使用 Guide 控件的高亮边框定位各页面的目标控件（按钮、列表、开关等）。

问题：多个引导步骤的目标控件因其父级 `IsVisible` 绑定到 ViewModel 属性（初始为 `false`）而不可见：

| 页面 | 不可见控件 | 原因 |
|------|-----------|------|
| MemberManagement | `StudentListBox`, `NewStudentRow` | `HasData` 为 false（无数据集） |
| VenueConfiguration | `LayoutTypePanel` | `HasSelectedVenue` 为 false（无会场） |
| StrategyConfiguration | `EditEnabledSwitch` | `HasDetail` 为 false（未选策略） |
| SnapshotHistory | `SnapshotListBox` | `HasSnapshots` 为 false（无快照） |

Guide 控件通过 NameScope 查找目标——即使控件不可见也能找到。因此 `MissingTargetBehavior="Center"` 回退不触发，Guide 尝试对零尺寸的不可见元素做高亮定位，引导卡片居中显示但无目标边框。

## Decision

**在 `OnboardingService` 中实现纯内存示例数据注入**——引导启动时直接向各页面 ViewModel 的 public 属性注入示例数据，引导结束时清除。不创建任何磁盘文件。

### 注入策略

| 页面 | 注入方式 | 延迟策略 |
|------|---------|---------|
| MemberManagement | 直接设置 `Students` ObservableCollection（6 名示例学生） | 同步（无异步 init 覆盖） |
| VenueConfiguration | 调用 `NewVenueCommand.Execute()` 创建空会场 | 命名/消息延迟到 `DispatcherPriority.Background`（`LoadVenueList` 异步覆盖） |
| StrategyConfiguration | 设置 `SelectedStrategy = Strategies[0]` | `DispatcherPriority.Background`（`LoadAsync` 异步填充 Strategies） |
| SeatingArrangement | 填充 `VenueItems`/`DatasetItems` + 直接构建 `SeatItems` | `DispatcherPriority.Background`（`LoadInitialDataAsync` 异步覆盖） |
| SnapshotHistory | 直接设置 `Snapshots` ObservableCollection | 同步（无异步 init 覆盖） |

### 延迟注入的必要性

多个 ViewModel 在构造函数中 fire-and-forget 启动异步初始化（`_ = LoadXxxAsync()`）。这些异步方法完成后会**创建全新** `ObservableCollection` 替换原有集合，覆盖同步注入的数据。`DispatcherPriority.Background` 是最低优先级的调度，保证在异步 continuation 之后执行。

### 清理策略

`CompleteOnboardingAsync()` 中调用 `ClearPageData()`：
- SeatingArrangement：清除 `SeatItems`、`VenueItems`、`DatasetItems`、`HasGenerated`
- SnapshotHistory：清除 `Snapshots`、`Venues`
- MemberManagement / VenueConfiguration：无持久副作用，由用户后续操作自然替换

## Alternatives Considered

### 方案 A：落盘示例数据文件

将示例学生、会场保存为真实的 `.roster.json` / `.venue.json` 文件，引导结束后删除。

- **Pros**：ViewModel 无需特殊处理，自然通过 facade 加载
- **Cons**：磁盘 I/O 开销；删除失败时残留文件；首次启动后若崩溃会留下脏数据；在已有用户数据的系统上重新引导会污染数据列表
- **Rejected**：用户明确要求"不创建文件，直接内存中加载"

### 方案 B：自动触发前置按钮 Command

在 `HandleStepOpening` 中检测目标不可见时，自动执行前置步骤的 `Button.Command`。

- **Pros**：无需了解 ViewModel 内部结构，通用性好
- **Cons**：`ImportButton.Command` 会打开文件对话框（阻塞 UI）；ListBox/ComboBox 选择无法自动触发；仅能解决 `NewVenueButton` → `LayoutTypePanel` 一个案例
- **Rejected**：覆盖面太窄，副作用不可控

### 方案 C：修改各页面 ViewModel 添加 `IDemoDataSeedable` 接口

- **Pros**：注入逻辑封装在各 ViewModel 内部，解耦清晰
- **Cons**：需修改 4+ 个 ViewModel 文件；接口定义需放在 Core/Application 层或 Presentation 层之间；引导系统本已是一个独立关注点，不应侵入业务 ViewModel
- **Rejected**：过度工程化，引导系统是 UI 层关注点

## Consequences

- **零磁盘痕迹**：引导结束无任何残留数据文件
- **不依赖 Infrastructure 层**：仅使用 Core 模型 + ViewModel public API，无架构分层违规
- **不修改任何 ViewModel**：纯外部注入，ViewModels 保持无感知
- **`DispatcherPriority.Background` 依赖**：延迟注入依赖 Avalonia 调度器的优先级语义。若未来 ViewModel 异步 init 改为更低的优先级，注入可能仍被覆盖。当前所有异步 init 使用普通优先级，Background 保证在本任务之后
- **ViewModel 私有字段限制**：SeatingArrangement 的 `_workspace`、`_currentLayout` 为私有字段，无法直接注入。当前方案直接构建 `SeatDisplayItem` 对象填充 `SeatItems`（座位预览可见，但右侧策略/消息面板为空——对引导场景可接受）
