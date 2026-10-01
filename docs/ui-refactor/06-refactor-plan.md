# 06 · 完整重构计划（Refactor Plan）

> 阶段 6 交付物 · 2026-10-01 · 状态：**待终审（门 6）**
> 依据：`00`–`05` 全部决策与证据；本计划只规划「如何实施」，实施本身待计划批准后另行启动（D9）。

## 1. 已锁定的输入

| 项 | 结论 | 来源 |
|---|---|---|
| 视觉/IA | 方向 B「方格纸」；新 IA（默认入口=排座工作台、取消独立 Home、自由点并入会场与布局、右栏页签化、侧栏分组+矮视口修复） | 03；门 3 |
| MVVM | **ReactiveUI.Avalonia 12.1.5 + ReactiveUI 25.1.1**（视图层普通 `UserControl`，VM 层 `ReactiveObject`） | 05 §2；spike #1 |
| Avalonia | 12.1.2 → **12.1.3**（补丁升级，不得降级） | spike |
| 画布 | 自绘 `Control.Render` + 几何命中；Grid/Polar/Freeform 全量支持 | 05 §5 |
| 性能目标 | M1–M6（02 §6） | 02 |
| 兼容性 | 7 种文件格式与用户数据全部向后兼容，必要时迁移器 | D10 |
| 测试 | xUnit v3 + MTP；新增 `Avalonia.Headless.XUnit` 12.1.3 | 05 §8 |

## 2. 目标与非目标

**目标**：消除 RC-1…RC-5 根因；落地方向 B 与新 IA；形成统一生命周期/脏检查/对话框门/繁忙状态；代码可测试、可维护。
**非目标**：不改业务规则与文件语义；不降级框架；不做 Web/桌面功能对齐（逐项保留现状差异）；本轮不实施。

## 3. 总体策略

1. **基座先行**：令牌/主题 → 生命周期与横切服务 → 画布控件 → 再逐页迁移。
2. **逐页 strangler**：每迁移一页即切换导航入口，应用始终可运行；旧实现与页面在迁移完成前并存。
3. **行为冻结**：迁移期间不新增功能；发现的缺陷进缺陷清单，随对应页面迁移修复。
4. **分支与提交**：沿用 `refactor/ui-overhaul`；每个里程碑一个可评审的 commit 区间；不主动合并 main（由用户决定）。
5. **双端同验**：每个里程碑在桌面构建 + WASM 发布 + MCP 实机验证（工具链沿用 `docs/ui-refactor/tools/`）。

## 4. 里程碑与任务

### M0 · 基座（约 3–4 天）
- 升级 Avalonia 12.1.3；引入 ReactiveUI.Avalonia；`App.axaml.cs`/两个壳的 `AppBuilder` 接入 `UseReactiveUI`。
- 新建 `Resources/Tokens/`（方向 B 明暗令牌）与 `Styles/Components.axaml`；迁移现有 3 个 StyleInclude。
- 横切件：`IPageLifecycle`、`IDialogGate`、`IBusyScope`；`SeatingCanvas` 骨架（几何接口 + 空渲染）。
- 删除静态可变状态（`ViewModelBase.Dialog`、`ShortcutConfig`、`FileDropHandler._host`、引导静态缓存）改为 DI 单例。
- 验收：`dotnet build` 双 TFM；`dotnet test` 全绿；WASM 启动截图；令牌切换明暗正常。
- 审核点 M0（用户抽检截图）。

### M1 · 画布与工作台内核（约 4–5 天）
- `SeatingCanvas` 完整实现：Render（座位/覆盖层/状态色）、指针命中、拖拽交换/入座、缩放平移矩阵、键盘虚拟焦点。
- 三布局几何统一（Grid 行列 / Polar 环角 / Freeform 坐标），复用 `ISeatGeometry`。
- 数据：座位不可变快照 + `LayoutVersion`；生成/回滚走增量替换。
- 验收：spike #2（300/800 座绘制与命中 <16ms/帧；拖拽无 >100ms 长任务）；三布局切换截图。
- 审核点 M1。

### M2 · 会场与布局页（含自由点并入）（约 3 天）
- 三布局编辑器合并页；参数 **120ms 去抖**（ReactiveUI `Throttle`）+ 单次重算；座位 ID 保留逻辑迁移。
- 生命周期：`OnEnterAsync` 加载 + 离开取消；脏检查用统一 `DirtyTracker`。
- 验收：M1 指标（≤50ms/次变更）。

### M3 · 排座工作台页（约 4 天）
- 三栏 + 右栏页签；命令栏/导出菜单；快照保存；撤销重做（沿用 `CommandHistory`）。
- 页面切换即时化（移除强制动画；保留 ≤120ms 非阻塞淡入）。
- 验收：M2（切页 INP ≤100ms）、M3。

### M4 · 名单 / 策略 / 快照（约 4 天）
- 名单：虚拟化恢复 + 轻量行编辑（显示/编辑切换）；脏检查与 3 按钮对话框接 `IDialogGate`。
- 策略：页签/详情迁移，优先级冲突提示保留。
- 快照：Transient 特例取消，改为缓存 + 显式刷新。
- 验收：M5；三页行为回归清单（01 文档功能点逐条勾选）。

### M5 · 设置 / 关于 / 引导 / 收尾（约 3 天）
- 设置分组卡片；关于页令牌化。
- 引导：`onboarding_config.json` 目标控件映射到新 IA；`OnboardingService` 改为通过 `IPageLifecycle`/公开命令交互，移除静态字段。
- 删除死代码与冗余动画（02 §动效清单）。
- 验收：首启引导全流程截图（MCP 自动化）；矮视口/移动断点回归。

### M6 · 测试、性能与文档（约 3 天）
- 新增 UI 测试项目（Headless）；VM 单测补齐脏检查/生命周期/合成命令；视觉回归基线。
- 重跑 5 场景 trace 对比 M1–M6；绑定诊断零警告。
- 更新 `docs/`（INDEX、CLAUDE.md、Design_Spec、WebDeployment 若有影响）。
- 审核点 M6（终验）。

## 5. 迁移策略（CTK → ReactiveUI 映射）

| 现有 | 迁移后 |
|---|---|
| `ObservableObject` + `[ObservableProperty]` | `ReactiveObject` + `[Reactive]`（源生成器） |
| `[RelayCommand]` / `AsyncRelayCommand` | `ReactiveCommand`（`IsExecuting`/取消内建） |
| 手写派生属性 + `RaisePropertyChanged` | `WhenAnyValue(...).Select(...).ToProperty(...)` |
| 构造器 fire-and-forget 加载 | `IPageLifecycle.OnEnterAsync(ct)`，订阅入 `CompositeDisposable` |
| `_dialogLock` + `Task.Delay(150)` | `IDialogGate`（ReactiveCommand `IsExecuting` 抑制） |
| 5 套脏检查 | `DirtyTracker`（JSON 快照/标记统一） |
| `CanLeaveAsync` 各写一遍 | 基类模板 + `ConfirmDiscardAsync`（`IDialogGate`） |
| `CanvasZoomPan` 行为 | `SeatingCanvas` 内部矩阵变换（行为退役） |
| 视图 `UserControl` | 保持 `UserControl`（trim 安全）；`WhenActivated` 用 `OnAttachedToVisualTree` + `CompositeDisposable` 等价实现 |

顺序：基座 → 工作台 → 会场 → 名单 → 策略/快照 → 设置/关于/引导；每页迁移后旧 VM 删除、导航切换。

## 6. 文件/模块级改动地图（要点）

- **新增**：`Resources/Tokens/{Colors,Spacing,Typography,Motion}.axaml`、`Controls/SeatingCanvas.cs`（+几何命中）、`Services/{IPageLifecycle,IDialogGate,IBusyScope,DirtyTracker}.cs`、`tests/SeatFlow.Presentation.Tests/`。
- **重构**：`App.axaml(.cs)`、`MainShellViewModel`、`MainView.axaml`（紧凑模式/抽屉）、9 个页面 VM+View（按 03 IA 重组）。
- **删除/退役**：`Behaviors/CanvasZoomPan.cs`、`ZoomOnScroll.cs`（并入画布）、`MainWindowViewModel`（未用）、`AnimateCardBounceAsync` 死代码、`HomeView/HomeViewModel`（内容并入工作台空态）。
- **保持**：`IApplicationFacade` 及以下各层接口不变；文件格式与迁移器不动；`ViewLocator` 约定保留。

## 7. 性能验收标准（重跑 02 §6 口径）

| # | 指标 | 目标 | 基线（02） |
|---|---|---|---|
| M1 | 会场参数单次变更阻塞 | ≤50ms（INP ≤100ms） | ~600–800ms / 315ms |
| M2 | 切页 INP（WASM） | ≤100ms | 493ms |
| M2b | 桌面换页端到端 | ≤250ms | 推断 ~800ms+ |
| M3 | 64 座画布首帧 | ≤300ms | 未单独计时 |
| M3b | 拖拽/缩放无 >100ms 长任务 | ≤100ms | 264ms |
| M4 | 启动长任务停止时刻 | ≤3s | ~6.3s |
| M5 | 240 行滚动单次操作 | ≤1 个 <100ms | 1×149ms |
| M6 | 10 次往返堆增长 | ≤10% | 待测 |

## 8. 测试策略

- **VM 单测**：生命周期状态机、DirtyTracker、命令状态、映射器；沿用 xUnit v3 + MTP。
- **Headless 组件测试**：画布命中/键盘导航、绑定、四态（Loading/Error/Empty/Content）。
- **视觉回归**：Headless Skia 基线图（关键页 × 明暗）；WASM 截图由 `tools/capture-baseline.mjs` 生成对比。
- **端到端**：MCP + CDP 既有工具链做关键流程脚本（生成/导出/回滚/引导）。
- **门禁**：`dotnet build -warnaserror`（或 IL 白名单）、绑定诊断零警告、M1–M6 trace 脚本。

## 9. 风险与回滚

| 风险 | 概率 | 影响 | 缓解/回滚 |
|---|---|---|---|
| ReactiveUI 迁移引入行为差异 | 中 | 高 | 逐页迁移 + 每页回归清单；单页可回退 CTK 实现（同一导航契约） |
| 自绘画布交互/可访问性缺口 | 中 | 中 | spike #2 先行；键盘虚拟焦点 + Automation 名称；必要时退回行级 ItemsControl |
| IL2026 裁剪告警扩大 | 低 | 中 | 视图层不用 `ReactiveUserControl`；告警白名单 + 回归测试 |
| 引导系统重写回归 | 中 | 高 | 行为清单先行；MCP 自动化跑全 24 步 |
| WASM 体积/启动超预期 | 低 | 中 | spike 已量化（+4.4%/+10%）；M6 复测 |
| 环境重置（浏览器/workload） | 高 | 低 | 一键脚本 `seed-demo-data.*`、工具链文档化（tools/README） |

## 10. 尚需执行的 Spike

1. ~~ReactiveUI WASM 裁剪/运行时~~ ✅ 已完成（05 §2.4）。
2. **自绘画布原型**（M1 前置）：300/800 座绘制/命中/拖拽/缩放 + 三布局。
3. **Headless 测试基建**（M6 前置）：项目模板 + MTP 参数 + 视觉基线流程。

## 11. 完成定义（DoD）

1. 全部 9 页面在新 IA 下功能逐条通过（01 清单勾选）。
2. M1–M6 达标（同口径 trace）。
3. 主线程无 >100ms 长任务（除启动与显式长任务）；绑定诊断零警告。
4. 明/暗 × 中/英 × 桌面/移动（≤900px）× 三布局 截图矩阵通过。
5. 无静态可变状态；订阅可退订；位图/Popup 全释放（堆快照 M6 达标）。
6. 文档同步完成；`docs/CLAUDE.md` 与根 `CLAUDE.md` 更新。
