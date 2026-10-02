# ADR-014: UI 技术架构 —— ReactiveUI 共存与自绘 SeatingCanvas

## 状态
已接受（部分取代 ADR-002：M0 起引入 ReactiveUI.Avalonia，ADR-002 对 ReactiveUI 的拒绝理由不再成立）

## 日期
2026-10-02

## 背景
2026-10 的 UI 彻底重构（M0–M6，记录见 `docs/UI_REFACTOR.md`）前，UI 层积累了三类结构性问题：

- **MVVM 范式不足以表达异步/去抖/取消**：会场预览约 40 个参数回调直接触发全量重建（RC-1），
  页面切换依赖强制动画与构造器 fire-and-forget 加载（RC-2），去抖、订阅作用域、命令互锁均需手写。
  ADR-002 当时以「项目不需要复杂响应式管道」为由拒绝 ReactiveUI，该论据已被实测卡顿推翻。
- **座位画布逐座位控件**：两层 `ItemsControl+Canvas` + 每座位一个控件 + `ReflectionBinding`，
  拖拽期实测 200–260ms 阻塞（RC-3），且入场初始化即触发全量模板实例化。
- **横切逻辑在页面间复制**：5 套脏检查、5 份 `CanLeaveAsync`、5 处 `_dialogLock + Task.Delay(150)`、
  多处静态可变状态（`ViewModelBase.Dialog`、`KeyboardShortcutHandler.ShortcutConfig` 等）。

需求拷问决策 D1/D6 允许较大范围重写并重新评估 MVVM 框架；D10 要求用户数据与文件格式全部兼容。

## 决策

1. **MVVM 双范式共存**：引入 `ReactiveUI.Avalonia 12.1.5`（传递 ReactiveUI 25.1.1，无 System.Reactive 依赖），
   Avalonia 升级至 **12.1.3**（补丁级）。视图层保持普通 `UserControl`（trim 安全，
   规避 `ReactiveUserControl<T>` 的 IL2026 反射激活警告）；VM 层新代码可用
   `ReactiveObject`/`[Reactive]`/`ReactiveCommand`/`WhenAnyValue`，存量 VM 与 `ViewModelBase`
   继续使用 CommunityToolkit.Mvvm 8.4 源生成器。两条范式在同一编译单元共存，不强制一次性迁移。
2. **座位画布自绘**：`SeatingCanvas : Control` 以 `Render(DrawingContext)` 统一绘制三种布局
   （Grid/Polar/Freeform，复用 Core `SeatGeometryHelper` 几何），指针坐标反算座位做命中测试；
   座位以不可变 `SeatLayoutSnapshot` + 版本驱动整体替换重绘，不做逐座位控件与逐项 Observable 绑定；
   内置拖拽交换、座位→垃圾桶、Ctrl+滚轮缩放（锚点保持）、空白拖拽平移；
   可访问性以方向键虚拟焦点 + `AutomationProperties.Name` 补偿。渲染侧采用按状态合批 Geometry、
   文本矢量化缓存、视口裁剪与手势期 LOD。
3. **页面生命周期与横切服务统一**：`IPageLifecycle`（`OnEnterAsync(ct)`/`OnLeaveAsync`/`IsDirty`/
   `InitializationTask`）取消构造器 fire-and-forget；`DirtyTracker` 统一脏检查；
   `IDialogGate` 取代 `_dialogLock`；`IBusyScope` 统一繁忙态；`IShellLayoutService` 提供 ≤900px
   紧凑断点；`IGuideSeedTarget` 把引导演示数据注入下沉到页面自身。清除全部静态可变状态。
4. **测试基建**：新增 `tests/SeatFlow.Presentation.Tests`（`Avalonia.Headless.XUnit 12.1.3` +
   `Avalonia.Skia` 真实绘制 + 视觉基线捕获）。该项目锁定 `xunit.v3 3.2.2`（Headless 包编译基线），
   其余测试项目保持 4.0.0；VM/服务单测与组件/像素回归并行。
5. **绑定与来源生成**：全量编译绑定（`ReflectionBinding` 实际使用清零）；优先 `Mode=OneTime`
   处理静态数据；不引入 DataGrid/TreeDataGrid/第三方主题库，保留 FluentTheme + 令牌层（方向 B）。

## 考虑的替代方案

### A · 保留 CommunityToolkit.Mvvm，自研等价小工具
- 优点：零新增依赖；无迁移成本；现有团队范式熟悉。
- 缺点：去抖、取消、派生状态、订阅作用域均需手写约 5 个小工具并在各页重复接线；
  与 RC-1/RC-2 的根因对齐度低。
- 结论：作为回退预案保留（单页可局部回退 CTK 实现），不作为主路线。

### B · ReactiveUI + `ReactiveUserControl<T>` 全家桶
- 优点：`WhenActivated` 生命周期作用域原生可用。
- 缺点：`ReactiveUserControl<T>` 的反射激活在裁剪下产生 2 × IL2026；视图层范式改造面更大。
- 结论：拒绝，改用「普通 UserControl + ReactiveObject VM」的 trim 安全组合；
  生命周期用 `OnAttachedToVisualTree/OnDetachedFromVisualTree` + `CompositeDisposable` 等价实现。

### C · 画布用 `ItemsRepeater + Canvas` 或保留逐座位控件
- 优点：可复用现有控件模板；元素天然可聚焦。
- 缺点：画布无滚动画布语义，虚拟化收益有限；300+ 控件命中测试与布局成本线性增长（RC-3 实测）。
- 结论：拒绝；自绘方案 300 座优化后稳态单帧 3.6–8.7ms、最长长任务 84.3ms。

### D · 引入第三方主题库（FluentAvalonia / SukiUI）或 DataGrid
- 缺点：设计已确定为令牌驱动，无需替换主题引擎；DataGrid 模板重、编辑体验与现有表单式行编辑不符。
- 结论：拒绝，保留 FluentTheme + `sf-*` 令牌样式。

## 后果

- **收益**：`Throttle` 使会场参数 INP 从 315ms 降至 18ms；自绘画布使拖拽/缩放无 >100ms 长任务；
  生命周期与横切服务消除了 5 套重复实现与全部静态可变状态；Headless 测试提供 UI 回归防线。
- **成本**：`_framework` 体积 +1.24MB（+4.4%）、启动长任务 +216ms（+10%，单次采样）；
  Avalonia 补丁升级 12.1.2 → 12.1.3；团队需适应响应式范式，两套范式并存期存在风格分裂风险
  （通过「新页面优先 ReactiveUI、存量保持 CTK」约束收敛）。
- **未达成/遗留**：切页 INP（M2，569ms）与启动停止时刻（M4，6.57s）未达标，
  归因于页面首次 XAML 构造挂载成本与启动 I/O，后续以视图常驻/分区懒加载收敛（详见 `docs/UI_REFACTOR.md` §7）。
- **兼容性**：无文件格式变化，无需迁移器；用户数据、设置与引导进度全部向后兼容。
- **与 ADR-002 的关系**：CTK 源生成器仍是 `ViewModelBase` 与存量 VM 的基础（该部分继续有效）；
  「拒绝 ReactiveUI」的结论被本 ADR 取代，MVVM 决策从单选变为双范式共存。
