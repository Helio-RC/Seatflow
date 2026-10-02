# SeatFlow Design Spec（方向 B · 方格纸）

> 2026-10 更新（M6）：本文档已对齐 UI 重构后的令牌体系（`Resources/Tokens/`）。
> 令牌是唯一真相源；本文档描述语义与用法，具体数值以 XAML 令牌文件为准。

## Principles

- **性能是设计约束**：任何交互必须先声明主线程成本（导航即时、参数去抖、画布自绘）。
- **一个工作台，不是九个孤岛**：以「选数据 → 生成 → 查看 → 导出」组织页面。
- **信息密度分层**：常驻仅当前任务必需信息，其余进页签/抽屉/详情区。
- **动效只为状态连续性服务**：默认零装饰动画。
- **每像素都要有令牌**：颜色/间距/圆角/字阶全部来自令牌表，禁止魔法值。
- **明/暗双主题同等打磨**；键盘可达、焦点可见、对比度 ≥4.5:1。

---

## Token System（唯一真相源）

| 层 | 文件 | 内容 |
|---|---|---|
| 颜色 | `Resources/Tokens/Colors.axaml` | `ThemeDictionaries`（Light/Dark）内为原始 Color；字典外用 `SolidColorBrush` 经 `DynamicResource` 引用；同时覆盖 Fluent `SystemControl*`/`SystemAccentColor*` |
| 间距/圆角/尺寸 | `Resources/Tokens/Spacing.axaml` | `SfSpace1..5`、`SfRadiusXs/Sm/Lg/Pill`、页面内边距、侧栏/行高/控件高/座位尺寸等 |
| 字阶 | `Resources/Tokens/Typography.axaml` | `SfFontXs/Sm/Md/Lg/Xl` |
| 动效 | `Tokens/MotionTokens.cs` | `Quick`(120ms) / `Normal`(180ms) / `Modal`(120ms)，经 `{x:Static}` 用于 Transitions |
| 组件样式 | `Styles/Components.axaml` | `sf-*` 类（按钮/卡片/页签/表格/设置行等） |

**规则**：页面/控件只使用 `{DynamicResource Sf*Brush}`（颜色）与 `{StaticResource SfSpace*/SfRadius*}`（尺寸）；禁止硬编码十六进制色值。

---

## Color System

### Light（方格纸）

| Token | Value | Context |
|-------|-------|---------|
| `SfBgColor` | `#F2F4F6` | 页面底 |
| `SfRailBgColor` / `SfSurfaceColor` | `#FFFFFF` | 侧栏 / 卡片面 |
| `SfSurface2Color` | `#F6F8FA` | 次级面（数据选择栏等） |
| `SfBorderColor` | `#D9E0E7` | 发丝边框（零装饰阴影） |
| `SfTextColor` / `SfTextDimColor` / `SfTextFaintColor` | `#16202B` / `#46586B` / `#7B8A9C` | 主 / 次 / 弱文本 |
| `SfAccentColor` | `#3B5BDB` | 强调（按钮/选中/链接） |
| `SfOkColor` / `SfWarnColor` / `SfDangerColor` | `#0E9F6E` / `#B45309` / `#D92D20` | 成功 / 警告 / 错误 |
| `SfCanvasBgColor` / `SfCanvasGridColor` | `#F5F7FA` / `#E3E9F0` | 画布底 / 24px 方格纸格线 |
| `SfSeatBgColor` / `SfSeatBorderColor` | `#FFFFFF` / `#C9D4E0` | 空座位 |
| `SfSeatOccupiedColor` / `Border` / `Fg` | `#E4E9FB` / `#8FA3E8` / `#2C3E9E` | 已分配座位 |
| `SfFixedColor` | `#3B5BDB` | 固定座位标记 |

### Dark（夜校工作台）

| Token | Value | Context |
|-------|-------|---------|
| `SfBgColor` | `#0B1020` | 页面底 |
| `SfRailBgColor` / `SfSurfaceColor` | `#0E1526` / `#121A2E` | 侧栏 / 卡片面 |
| `SfBorderColor` | `#22304A` | 边框 |
| `SfTextColor` / `SfTextDimColor` | `#E7ECF5` / `#9AA8C0` | 主 / 次文本 |
| `SfAccentColor` | `#7C93FF` | 强调（暗色提亮） |

> 状态色在暗色下统一提亮（由 `ThemeDictionaries` 提供），状态必须同时有文字/图标语义，不能只靠颜色。

---

## Typography

| Token | Size | 用法 |
|---|---|---|
| `SfFontXl` | 20 | 页面标题（`.sf-title`） |
| `SfFontLg` | 16 | 区块标题（`.sf-h3`） |
| `SfFontMd` | 13.5 | 正文/表格默认（`.sf-md`） |
| `SfFontSm` | 12.5 | 辅助文本/列表项（`.sf-sm`） |
| `SfFontXs` | 11 | 标签/徽标（`.sf-xs`） |

- 字体族：`fonts:Inter#Inter,Microsoft YaHei UI,PingFang SC,Noto Sans CJK SC,WenQuanYi Micro Hei,sans-serif`（WASM 由嵌入字体 + Noto Sans SC 回退保证 CJK）。
- 数字统一 tabular（`.sf-num`，`FontFeatures=tnum`），用于人数/座位数/优先级对齐。

---

## Spacing / Radius / Sizes

| Token | Value | | Token | Value |
|---|---|---|---|---|
| `SfSpace1` | 4 | | `SfRadiusXs` | 3 |
| `SfSpace2` | 8 | | `SfRadiusSm` | 5 |
| `SfSpace3` | 12 | | `SfRadiusLg` | 8 |
| `SfSpace4` | 16 | | `SfRadiusPill` | 999 |
| `SfSpace5` | 24 | | | |

页面内边距 16；卡片内边距 16；命令栏 padding `16,8`；侧栏 172（展开）/ 64（折叠）；行高 32；控件高 30；座位 38；画布格线步长 24。

---

## Motion Policy（M6）

| 动效 | 允许 | 时长 | 说明 |
|---|---|---|---|
| 导航/页面切换 | ❌ | — | 即时切换（无强制淡出/间隔）；切页后视图弱引用缓存 |
| 侧栏折叠 | ✅ | 150–180ms（`Normal`） | 状态语义 |
| 悬停/按下反馈 | ✅（仅颜色） | 120ms（`Quick`，BrushTransition） | 背景/边框色过渡 |
| 对话框/浮层出现 | ✅ | 120ms（`Modal`） | 层级语义 |
| 引导卡片 | ✅ | 150ms | 保留 |
| 工具栏折叠/卡片弹跳等装饰动画 | ❌ | — | 已删除 |
| reduced motion | 必须 | — | 检测平台设置后全部禁用 |

---

## IA & Layout

- **7 页**：排座工作台（默认入口）/ 人员管理 / 会场与布局 / 策略配置 / 历史快照 / 设置 / 关于。
- **侧栏分组**：工作流（排座工作台）、资料（人员管理、会场与布局）、规则（策略配置）、记录（历史快照）、底部设置/关于；中部可滚动，修复矮视口覆盖（I-01）。
- **排座工作台三栏**：左「数据选择」→ 中 `SeatingCanvas` → 右检查器 `TabControl`（策略 / 未分配 / 记录 / 消息）。
- **紧凑断点 ≤900px**（`IShellLayoutService`）：导航转左侧抽屉，页面上下文（数据/检查器/数据集列表）转右侧抽屉（`SideDrawerState`），遮罩点击关闭；桌面宽度 <750px 侧栏自动折叠。
- 页面结构：命令栏（顶部）→ 内容 → 状态栏（底部 28px）。

---

## Canvas（SeatingCanvas）

- 自绘 `Control.Render`：方格纸背景（24px 格线）、座位（38px）、覆盖物（讲台/门/禁用虚线）。
- 状态：空座 / 已分配 / 固定（角标）/ 选中（强调描边）/ 拖拽悬停（虚线强调）/ 失效（警告描边）/ 禁用（虚线+灰面）。
- 交互：拖拽交换、座位↔垃圾桶、Ctrl+滚轮缩放（0.2–3.0，锚点保持）、空白拖拽平移、方向键虚拟焦点 + Enter 激活。
- 大数据量：按状态合批 `GeometryGroup`、文本矢量化几何 + 视口裁剪 + 手势期 LOD。

---

## Interaction

- Hover：背景/边框色 120ms 过渡。
- Selected：导航/列表项 AccentSoft 底 + 强调文字（WM 显式属性绑定，避免动态类绑定在 WASM 失效）。
- Focus：可见焦点环（≥3:1）。
- 状态四态：Loading（遮罩/忙碌作用域）、Error（错误横幅/弹窗）、Empty（统一空态卡）、Content。
- `AutomationProperties.Name` 覆盖图标按钮；座位画布提供方向键虚拟焦点作为可访问性补偿。

---

## Dependencies

- **UI**：Avalonia **12.1.3**（FluentTheme 基座 + 方向 B 令牌覆盖）
- **MVVM**：CommunityToolkit.Mvvm 8.4 + **ReactiveUI.Avalonia 12.1.5**（B 路线，视图层普通 `UserControl`）
- **Icons**：`FluentIcons.Avalonia` 2.1.341（清单见 [Fluent_Icons.md](Fluent_Icons.md)）
- **字体**：Inter（`Avalonia.Fonts.Inter` / WASM 嵌入）
