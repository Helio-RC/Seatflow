# 05 · 技术选型（Tech Selection）

> 阶段 5 交付物 · 2026-10-01 · 状态：**待审核（强制门 5）**
> 口径：结论必须有证据（包版本/依赖图/官方文档/现有代码/编译 spike），不预设结论（决策 D6）。
> 约束：不得降级 Avalonia/.NET；双端（桌面 + WASM）同等重要。

> **更正记录（2026-10-01，由用户指出后复核）**
> 初稿仅查询了旧包名 `Avalonia.ReactiveUI`（最新 11.3.8，无 12.x），据此错误结论「ReactiveUI 无 Avalonia 12 集成」。
> 复核后确认官方新包 **`ReactiveUI.Avalonia` 12.1.5**（2026-09-28 发布）存在，且：
> ① 依赖 **Avalonia ≥ 12.1.3**（项目需从 12.1.2 补丁升级）；
> ② **不再依赖 System.Reactive**（依赖图为 ReactiveUI 25.1.1 / ReactiveUI.Core / Primitives / SourceGenerators）；
> ③ 编译级 spike 在 **net10.0 与 net10.0-browser 双 TFM 通过（0 警告）**，含 `[Reactive]` 源生成器、`ReactiveCommand`、`ReactiveUserControl<T>`。
> `spikes/reactiveui-probe/` 为可复跑的 spike 工程。以下评估已按新证据重写。

## 1. 结论速览（修订）

| 主题 | 结论 | 证据强度 |
|---|---|---|
| MVVM 框架 | **B · 采用 ReactiveUI.Avalonia 12.1.5**（spike #1 已通过：+1.24MB / +10% 启动 / 2×IL2026 有缓解路径）；A（保留 CTK）为回退预案 | **强**（编译 + 运行时 spike + 依赖图，见 §2） |
| 去抖/取消/派生状态 | A 路线：自研 `Debouncer`+`AsyncScope`（~50 行）；B 路线：`Throttle`/`WhenAnyValue`/`WhenActivated` 原生覆盖 | 强（§2.4） |
| 数据绑定 | 无论 A/B：全量编译绑定、禁止 ReflectionBinding、`x:DataType` 强制；静态数据 `OneTime` | 强（官方 + 现状） |
| 集合更新 | 列表 `ObservableCollection` 增量；画布用**不可变座位快照 + 版本号**触发重绘，不做逐座位控件 | 强（02 诊断） |
| 座位画布渲染 | **自绘 `Control`（`Render(DrawingContext)` + 几何命中测试）**；Grid/Polar/Freeform 统一走几何模型 | 中高（§5 对比） |
| 控件库 | 保留 FluentTheme + 令牌层；**不引入 DataGrid/TreeView/第三方主题库**；保留 FluentIcons | 强（03 设计） |
| 引导控件 | 短期保留 CodeWF.AvaloniaControls；阶段 6 评估自研 overlay | 中 |
| 状态/生命周期 | 页面状态机 + `OnEnterAsync/OnLeaveAsync` + 取消令牌；清除静态可变状态；跨 VM 消息；异步命令 | 强（01/02） |
| 测试 | VM 单测（现有 xUnit v3 + MTP）+ **Avalonia.Headless.XUnit 12.1.3**（依赖 xunit.v3 3.2.2，已验证）+ headless Skia 视觉回归 + 现有 WASM trace 性能门禁 | **强** |

## 2. MVVM 框架评估（修订，D6 重点）

### 2.1 事实（2026-10-01 复核）

| 项 | 事实 |
|---|---|
| 官方集成包 | `ReactiveUI.Avalonia` **12.1.5**（2026-09-28） |
| 依赖 | `Avalonia >= 12.1.3`；ReactiveUI 25.1.1 + ReactiveUI.Core + Primitives + SourceGenerators 4.2.0；**无 System.Reactive 包** |
| 目标框架 | 包内含 net8.0 / net9.0 / net10.0 / net11.0 资产；net10.0-browser 解析成功 |
| 编译 spike | `spikes/reactiveui-probe`：`[Reactive]` 源生成器、`ReactiveCommand`、`ReactiveUserControl<T>` 双 TFM 编译通过，0 警告 |
| 旧包 `Avalonia.ReactiveUI` | 11.3.8（已由新包名取代） |

### 2.2 两条路线的成本/收益

| 维度 | A · 保留 CommunityToolkit.Mvvm 8.4.2 | B · 采用 ReactiveUI.Avalonia 12.1.5 |
|---|---|---|
| 新增运行时依赖 | 0 | ReactiveUI + Core + Primitives + SourceGenerators（若干程序集） |
| Avalonia 版本 | 维持 12.1.2 | 需升 12.1.2 → **12.1.3**（补丁级，允许） |
| VM 迁移面 | 无 | ~20 个 VM 重写（本次重构本就重写 UI 层，迁移成本被吸收大半） |
| 命令 | `[RelayCommand]`/`AsyncRelayCommand` | `ReactiveCommand`（自带 `IsExecuting`、`ThrottleFirst`、取消） |
| 属性 | `[ObservableProperty]` | `[Reactive]`（同源生成器范式，机械替换为主） |
| 派生状态 | 手写属性 + 通知 | `WhenAnyValue`/`CombineLatest` 一等公民 |
| 去抖（RC-1） | 自研 `Debouncer`（~30 行） | `Throttle(TimeSpan)` 原生 |
| 生命周期/订阅（RC-2/4） | 自研 `AsyncScope` + 手工退订约束 | `WhenActivated`/`WhenAny` 作用域化，天然可退订 |
| WASM 裁剪/AOT | 已证安全 | **未验证**（v25 无 System.Reactive，风险降低，但仍需 spike） |
| 团队范式 | 现有代码全员熟悉 | 需学习响应式思维；两套范式并存期风险 |
| 生态维护 | Toolkit 稳定 | ReactiveUI 25 活跃（2026-09 仍在发版） |

### 2.3 关键判断

1. **可用性不再是障碍**：B 路线技术可行（双 TFM 编译通过、官方包、补丁级版本要求）。
2. **收益与根因高度对齐**：RC-1（Throttle）、RC-2/4（WhenActivated 生命周期与订阅作用域）、命令状态（生成/导出互锁）都能直接用 ReactiveUI 原生能力表达；A 路线需自研等价小工具。
3. **成本集中在迁移而非技术**：本次是 UI 层彻底重构，VM 本就要重写——若长期倾向响应式，**现在是一次性切换的最佳时机**；若追求最小风险，A 路线以 ~5 个小工具即可覆盖全部已知需求。
4. **剩余未知**：B 路线在 WASM 裁剪/运行时下的实际表现（体积、启动、事件流开销）未测，必须 spike（§10）。

### 2.4 Spike #1 结果（ReactiveUI WASM 裁剪/运行时）——**已执行，通过**

工程：`spikes/reactiveui-wasm/`（同一代码库，`-p:UseReactiveUi=false` 产出对照组）。
环境：Avalonia 12.1.3 + ReactiveUI.Avalonia 12.1.5 + ReactiveUI 25.1.1，Release 发布（`WasmBuildNative=true` 原生链接），内网 HeadlessChrome + MCP。

| 项 | RUI 变体 | 基线（纯 INPC） | 结论 |
|---|---|---|---|
| 发布 | ✅ 成功 | ✅ 成功 | 可发布 |
| 运行时 | ✅ 渲染 + 点击 → `[SPIKE] increment -> 1` → 300ms 后 `[SPIKE] throttled -> …`（`WhenAnyValue+Throttle` 生效） | ✅ 点击 → `increment -> 1` | **响应式全链路在 WASM 上运行正常** |
| `_framework` 体积 | 29.68 MB | 28.44 MB | **+1.24 MB (+4.4%)**，9 个程序集 |
| 启动长任务（~18s 窗口） | 9 个 / 2345 ms（最长 1087 ms） | 9 个 / 2129 ms（最长 913 ms） | **+216 ms (+10%)**，单次采样 |
| 裁剪警告 | **2 × IL2026**（仅 `ReactiveUserControl<T>` 的反射激活） | 0 | 有缓解路径，见下 |
| 运行时异常 | 无 | 无 | — |

**IL2026 缓解路径（迁移时采用）**：
- 视图保持普通 `UserControl`（ReactiveUI 仅用于 VM 层 `ReactiveObject`/`ReactiveCommand` 与绑定），可完全规避该警告；
- 如需 `WhenActivated` 生命周期，以 `OnAttachedToVisualTree/OnDetachedFromVisualTree` + `CompositeDisposable` 等价实现（trim 安全）；
- 若保留 `ReactiveUserControl`：spike 证明运行时可用，但需在裁剪策略中显式接受/抑制该警告并加回归测试。

### 2.5 建议（Spike 后更新）

- **采纳 B（ReactiveUI.Avalonia 12.1.5）**：技术、运行时、体积（+4.4%）、启动（+10%）均在可接受范围；
  视图层采用「普通 UserControl + ReactiveObject VM」的 trim 安全组合。
- 需要接受的成本：Avalonia 补丁升级 12.1.2 → 12.1.3；~20 个 VM 迁移（本次重构本就重写）。
- 回退预案：若迁移中出现未预期阻塞，可局部回退为 CTK 实现（两条路线 VM 层可共存于同一编译单元，但不推荐长期混合）。

## 3. 数据绑定策略

| 规则 | 说明 | 现状动作 |
|---|---|---|
| 编译绑定 | Avalonia 12 默认开启；所有 View/DataTemplate 声明 `x:DataType` | 全量核查，**删除 `SeatingArrangementView.axaml` 的 `ReflectionBinding`**（3 处） |
| 静态数据 | `Mode=OneTime` | 关于页/常量文案 |
| 转换器 | 仅用于无法用属性表达式表达的转换；避免分配型转换器（`FilePathToBitmapConverter` 需缓存 + Dispose） | 清理 |
| 绑定诊断 | 开发构建开启绑定日志，零警告作为 CI 门禁 | 阶段 6 |

## 4. 集合与状态更新策略

| 场景 | 方案 |
|---|---|
| 名单表格（数百行） | `ListBox` + `VirtualizingStackPanel`（恢复虚拟化）；行内编辑用「显示 TextBlock / 编辑 TextBox」轻量模式 |
| 快照/未分配等长列表 | `ObservableCollection` 增量（add/remove/replace），批量替换用整体赋值 |
| 座位集合 | **座位模型与视图解耦**：`IReadOnlyList<SeatSnapshot>` + `LayoutVersion`；变更时替换引用并 `InvalidateVisual()`（单控件重绘），不做逐座位 Observable 绑定 |
| 会场参数预览 | 去抖 120ms + 单次重算（§2.4 `Debouncer`） |

## 5. 座位画布渲染技术裁决

| 方案 | 300 座控件数 | 布局/命中成本 | 拖拽/缩放 | 可访问性 | 结论 |
|---|---|---|---|---|---|
| A 每座位一个控件（现状） | 300+ | 高（测量/排列/命中线性） | 需逐控件事件 | 天然可聚焦 | ✗（RC-3 实测 200–260ms 阻塞） |
| B `ItemsRepeater` + `Canvas` | 300+（虚拟化收益有限，画布无滚动视口语义） | 中高 | 同 A | 一般 | △ 备选 |
| C **自绘 `Control.Render` + 几何命中** | **1** | 低（一次绘制；命中为几何计算） | 统一处理（指针→坐标→座位） | 需自建虚拟焦点/键盘导航 | ✅ 推荐 |

**方案 C 实施细则（阶段 6 计划纳入）**：
- `SeatingCanvas : Control`：
  - `Render(DrawingContext)` 按 `LayoutVersion` 绘制座位矩形/圆点、状态色（令牌）、覆盖层（讲台/门/禁用）；
  - `OnPointerPressed/Moved/Released` 将坐标反算为座位（Grid：行列映射；Polar：极坐标；Freeform：最近点 + 命中半径）；
  - 选中/悬停/拖拽目标作为 StyledProperty，变化即局部重绘；
  - 键盘：方向键移动虚拟焦点（可访问性补偿，`AutomationProperties` 提供名称）。
- 三种布局共用同一几何接口（`ISeatGeometry` 已存在），渲染与命中均按几何实现，**Polar/Freeform 与 Grid 同等完整支持**。
- 缩放/平移：沿用现有 `CanvasZoomPan` 思路改为画布内变换矩阵（`Matrix`），缩放不重建视觉树。

## 6. 控件与主题库

- **主题**：`FluentTheme` 基座 + 方向 B 令牌字典（`ThemeDictionaries` Light/Dark），不引入第三方主题包（FluentAvalonia/SukiUI 等）——设计为令牌驱动，无需换主题引擎。
- **数据控件**：不使用 `DataGrid`（现设计为表单式可编辑行 + ListBox 虚拟化；DataGrid 模板重、编辑体验不同）；不引入 `TreeDataGrid`（无树形数据需求）。
- **图标**：保留 `FluentIcons.Avalonia` 2.1.341。
- **引导**：保留 `CodeWF.AvaloniaControls`（阶段 3 已决定引导随新 IA 重写目标；是否替换控件在阶段 6 评估，不作为强制项）。
- **`Svg.Controls.Skia.Avalonia`**：仅用于品牌/静态图；如无使用点可移除（阶段 6 核查）。**M6 结论：全仓零使用，已移除**（同时消除其传递的旧版 Linux native 与托管 SkiaSharp 4.152.0 的版本错配，见 08 日志 M6 评审修复）。

## 7. 状态、生命周期、命令、消息、DI

| 主题 | 决策 |
|---|---|
| 页面生命周期 | `IPageLifecycle { Task OnEnterAsync(ct); Task OnLeaveAsync(); bool IsDirty; }`；禁止构造器异步加载 |
| 页面实例 | 全部 Singleton + 显式刷新（进入时版本比对）；取消 SnapshotHistory Transient 特例 |
| 取消与释放 | 每页持有 `CancellationTokenSource`；实现 `IAsyncDisposable`；位图/Popup 必须释放 |
| 静态状态 | 全部清除：`ViewModelBase.Dialog`、`KeyboardShortcutHandler.ShortcutConfig`、`FileDropHandler._host`、`OnboardingService` 静态缓存 |
| 命令 | `AsyncRelayCommand` + `IncludeCancelCommand`（长任务：导入/导出/生成） |
| 消息 | `WeakReferenceMessenger`（ADR-002 计划落地）：导航/设置变更等跨 VM 通知 |
| DI | 现有 `Microsoft.Extensions.DependencyInjection`；新增 `IDebouncer`、`IDialogGate`、`IBusyScope`、UI 测试项目 |

## 8. 测试与验收工具链

| 层 | 工具 | 证据/状态 |
|---|---|---|
| VM/服务单测 | xUnit v3 + FluentAssertions + NSubstitute（现有 3 项目，381 用例） | ✅ 已验证 |
| 控件/绑定测试 | **Avalonia.Headless.XUnit 12.1.3**（`[AvaloniaFact]`/`[AvaloniaTestApplication]`） | ✅ 依赖图显示 `xunit.v3.extensibility.core 3.2.2`，与 MTP 兼容 |
| 视觉回归 | Headless + Skia（`UseHeadlessDrawing=false` + `CaptureRenderedFrame`），基线图入库 | 计划（阶段 6） |
| 端到端/性能 | 现有 WASM + CDP trace 工具链（`docs/ui-refactor/tools/`）与 M1–M6 指标 | ✅ 已验证 |
| 绑定健康 | Debug 绑定诊断日志零警告 | 计划（CI 门禁） |

## 9. WASM / 裁剪与 AOT

- **A 路线（CTK）**：无新增裁剪风险面（当前状态）。
- **B 路线（ReactiveUI）**：v25 已移除 System.Reactive 依赖，风险小于旧版本；但新增程序集在 WASM 裁剪下的反射/动态行为**尚未验证**，为采纳 B 的前置 spike（§10-1），验收项：publish 成功、0 trim 警告（或已知白名单）、运行时事件流正常、包体积增幅记录在案。
- 自绘画布为纯托管绘制，无反射、无动态代码，裁剪安全（A/B 通用）。
- 保留 `JsonSerializerIsReflectionEnabledByDefault=true`（仓储现有反射式序列化；热点路径可后续引入 SourceGen，阶段 6 可选）。
- 新 UI 测试项目仅桌面 TFM，不参与 WASM 发布。

## 10. 需要 spike 验证的项（阶段 6 计划任务）

1. ~~**ReactiveUI WASM 裁剪/运行时 spike**~~ ✅ **已完成**（见 §2.4；通过，附体积/启动/裁剪警告数据与缓解路径）。
2. **自绘座位画布原型**：300/800 座下的绘制与命中耗时；拖拽/缩放交互回归；Grid/Polar/Freeform 三布局。
3. **Headless 测试基建**：与 MTP 参数的集成方式（测试项目模板 + CI 命令）。
4. （可选）绑定诊断零警告门禁的落地方式。

## 11. 对阶段 6 的输入

- 依赖变化（**已按 B 路线选定**）：新增 `ReactiveUI.Avalonia` 12.1.5（+ ReactiveUI 25.1.1 家族），Avalonia 补丁升级至 12.1.3；测试包新增 `Avalonia.Headless.XUnit` 12.1.3。
- 移除/整改：`ReflectionBinding` 3 处、静态状态 6 处、构造器异步 8 处、Transient 特例 1 处。
- 性能门禁：沿用 02 §6 的 M1–M6 与 trace 工具链。
- 兼容性：无文件格式变化，无需迁移器（D10 不变）。
- **路线裁决：B · ReactiveUI.Avalonia**（spike #1 通过；视图层采用 trim 安全组合，见 §2.5）。
