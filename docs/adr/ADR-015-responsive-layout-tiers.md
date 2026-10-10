# ADR-015: 响应式三档布局与手机档导航

## 状态
已接受（扩展 ADR-014 第 3 条中 `IShellLayoutService` 的单一紧凑断点设计）

## 日期
2026-10-10

## 背景

M0–M6 UI 重构（ADR-014）只引入了单一 900px 紧凑断点：侧栏与页面面板在 ≤900px 转为覆盖抽屉。
上线后暴露三类窄屏可用性问题：

- **浏览器端（WASM）手机不可用**：360–430px 视口下，抽屉固定 240–320px、对话框固定 400–520px、
  会场配置页固定 240px 列与 170px 字段、命令栏按钮链不可换行，内容大量溢出或仅剩几十像素可用。
- **桌面端到不了手机比例**：`MainWindow` 硬下限 `MinWidth=550 / MinHeight=600`，无运行时放宽路径，
  小窗用户与手机尺寸之间出现"断层"，无法用同一套布局验证。
- **会场配置页完全没有紧凑适配**：全仓库唯一不消费 `IShellLayoutService` 的内容页，
  固定 `ColumnDefinitions="240,*"`、27 处 `sf-field` 固定 170px、`MinWidth=760` 坐标表。

同时，WASM 设计文档曾把"移动端适配"列为非目标；本决策将其推翻，明确手机纵向为支持场景。

## 决策

1. **三档断点体系**：`LayoutTier { Desktop >900 / Compact 600–900 / Phone ≤600 }` 取代布尔 `IsCompact`。
   断点常量集中在 `LayoutBreakpoints`（`Compact=900`、`Phone=600`），由 `MainShellViewModel.OnWindowWidthChanged`
   解析（含 `width<=0` 初始 Bounds 守卫）；`IShellLayoutService` 暴露 `Tier` 与派生 `IsNarrow`/`IsPhone`，
   页面 VM 沿用「服务驱动 + 绑定」模式，不引入视图层宽度触发器。
2. **手机档外壳导航**：底部 Tab 栏（排座 / 人员 / 会场 / 策略 / 更多；`page_navigation.json` 禁用页
   在 Tab 与「更多」菜单中隐藏）；手机档无外壳顶栏与侧栏；紧凑档保留「顶栏 + 左抽屉」现状；
   桌面档侧栏内联不变。新增样式类 `sf-tabbar`/`sf-tab` 与令牌 `SfTabBarHeight=56`。
3. **页面面板三态**：`SideDrawerState` 升级为桌面内联 → 紧凑右抽屉（320px + 遮罩）→
   手机全屏覆盖（`Width=NaN` 全宽、无遮罩、`IsFullScreen` 驱动「返回头部」）。排座工作台的
   自研双抽屉同步三态化。全屏覆盖只盖内容区，底部 Tab 保持可点。
4. **命令栏三档行为**：桌面显示全部按钮文字；紧凑档图标化（`CommandTextVisible=false`）并把
   混合按钮收紧为 `sf-icon`（30px），工具栏容器换行兜底（WrapPanel）；手机档每页保留标题 +
   4–6 个高频图标直排，低频/带子菜单动作收进 `⋯` 溢出菜单（`MenuFlyout`）。各页「主操作 vs ⋯」
   分配：排座（数据/检查器/撤销/重做/生成 + ⋯保存/导出/创建空白）、人员（数据集/导入(更新)/保存
   + ⋯导出/模板/另存/卸载）、快照（列表/创建 + ⋯刷新/批量删除）、策略（列表/恢复默认/保存）、
   设置（保存/恢复默认）、会场（列表/重命名/删除/保存）。
5. **对话框集中钳制**：新增 `DialogSizing`。桌面壳按 owner 窗口钳制宽度（`min(设计, owner-32)`），
   高度按窗口类型处理——固定高度窗口直接钳制、`SizeToContent="Height"` 窗口（DialogWindow/InputWindow/
   SeatSetsSelectionWindow）钳制 `MaxHeight` 上限；浏览器壳按 MainView 视口钳制 overlay 内容尺寸
   （宽度显式取 `min(设计, vw-32)` 保持视觉统一，高度 `min(设计, vh-48)` 并外包 `ScrollViewer`）；
   对话框内容移除固定 `Width` 改 `MinWidth/MaxWidth`；`sf-modal` 最小宽 420→280。
6. **桌面最小尺寸**：`MinWidth 550→360`、`MinHeight 600→480`，使桌面窗口可缩至手机宽度，
   双壳共用同一响应式行为（桌面 360–600 即手机档）。
7. **验证方式**：Headless 布局不变量测试（三档可见性、命令栏按钮不溢出视口、全屏面板贴合视口）、
   手机尺寸视觉基线（360×640、390×844）、手工矩阵（Chrome 移动仿真 360/390/430 + 真机）。
   `TestAppBuilder` 补注册 `WithInterFont()`（MainView 根级 Inter 字体在 headless 下缺失时崩溃）。
8. **引导适配**：手机档引导卡片定位 `Right→Bottom`（`GuidePlacementMode`）；目标是侧栏按钮的
   步骤在手机档沿用既有全局 `MissingTargetBehavior=Center` 配置回退（未新增机制）。

## 非目标 / 后续增强

- 系统返回键 / 浏览器 history 集成（`pushState`/`popstate` 关闭全屏面板）——留作后续增强。
- SeatingCanvas 双指缩放手势（本期仅验证指针事件下拖拽/缩放条可用）。
- 引导步骤在手机档的目标解析增强（Tab 按钮命名目标）。
- 会场 Freeform 坐标表的手机档卡片化视图（本期维持 `MinWidth=760` + 横向滚动）。
- 引导卡片在 360px 视口下仍按最大 420px 设计宽渲染（Popup 自动翻转兜底），未做横向钳制。

## 后果

- 断点与抽屉宽度不再散落：断点常量集中 `LayoutBreakpoints`，抽屉默认宽度在 `SideDrawerState`；
- `IsCompact` 全量迁移为 `Tier/IsNarrow/IsPhone`（7 个 VM + 5 个页面 XAML + 测试）；
- 双壳行为一致（桌面/浏览器同代码路径），WASM 移动端从"非目标"转为支持场景；
- 文档联动：`Design_Spec.md`（断点/令牌/样式类）、`UI_REFACTOR.md`（M7 记录）、`AGENTS.md`、
  `WebDeployment.md`、`CHANGELOG.md`、`docs/INDEX.md`（ADR 清单）。

## 关联

- `docs/adr/ADR-014-reactiveui-and-canvas-architecture.md`（`IShellLayoutService` 原始设计）
- `docs/presentation/Design_Spec.md`、`docs/UI_REFACTOR.md`、`docs/WebDeployment.md`
- `src/SeatFlow.Presentation.Avalonia/Services/IShellLayoutService.cs`（`LayoutTier`/`LayoutBreakpoints`）
- `src/SeatFlow.Presentation.Avalonia/Services/DialogSizing.cs`
