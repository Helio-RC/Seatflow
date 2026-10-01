# 01 · 当前 UI 功能盘点（Current UI Inventory）

> 阶段 1 交付物 · 2026-10-01 · 状态：待审核（审核门 1）
> 范围：`src/SeatFlow.Presentation.Avalonia`（共享 UI 库）+ `SeatFlow.Desktop` / `SeatFlow.Browser` 壳
> 方法：静态代码审阅（行号证据）+ WASM 运行时核对（基线截图 `assets/before/`，1200×800，zh-CN 浅色）
> 完整性声明：覆盖 9 个页面、壳与导航、全部对话框、Behaviors、自定义控件、动画、数据流、生命周期与静态状态。
> 未覆盖：桌面壳的 Velopack 更新 UI、托盘/单实例等非 UI 逻辑（不属本次重构对象）。

## 1. 概览

### 1.1 页面规模（实测 `wc -l`）

| 页面 | ViewModel | View (axaml) | code-behind | 注册生命周期 |
|---|---|---|---|---|
| Shell（MainView） | `MainShellViewModel.cs` 236 | `MainView.axaml` 564 | `MainView.axaml.cs` 48 | Singleton |
| MainWindow（桌面壳） | `MainWindowViewModel.cs` 7（未用） | `MainWindow.axaml` 25 | `MainWindow.axaml.cs` 99 | Singleton |
| Home | 259 | 179 | 28 | Singleton |
| MemberManagement | 930 | 754 | 72 | Singleton |
| VenueConfiguration | 1231 | 1228 | 61 | Singleton |
| FreeformManagement | 622 | 436 | 12 | Singleton |
| StrategyConfiguration | 820 | 332 | 12 | Singleton |
| SeatingArrangement | 1219 | 819 | **262** | Singleton |
| SnapshotHistory | 611 | 455 | 12 | **Transient** |
| Settings | 762 | 620 | 12 | Singleton |
| About | 148 | 347 | 12 | Singleton |
| 引导服务 | `Services/OnboardingService.cs` 846 | — | — | Singleton |

合计约 **22.3K 行**（含 `Lang/Resources.Designer.cs` 824 行生成代码等）。

### 1.2 控件使用统计（全项目 .axaml）

- **无 `DataGrid`、无 `TreeView`/`TreeDataGrid`、无 `ContextMenu`**（0 处）。
- 表格/列表一律 `ListBox` + `ItemsControl` 手写。
- `Canvas` 用于 4 个页面共 6 处：SeatingArrangement（2 层）、VenueConfiguration（2 层）、SnapshotHistory、FreeformManagement。
- `ItemsControl` 14 处；`GridSplitter` 3 处；`MenuFlyout` 1 处（座位页导出菜单）；`Flyout` 2 处（名单页紧凑模式）。
- 图标库：`FluentIcons.Avalonia`；引导控件：`CodeWF.AvaloniaControls`。

## 2. 壳与导航

### 2.1 结构

- 桌面：`MainWindow.axaml`（25 行）仅承载 `ShellHost` ContentControl + 32px 扩展标题栏留白；`MainWindow.axaml.cs` 处理关闭拦截、窗口位置/尺寸保存、最小/最大化事件转发（`Views/MainWindow.axaml.cs:37-97`）。
- 浏览器：`MainView` 直接作为 `ISingleViewApplicationLifetime.MainView` 挂载；`App.axaml.cs:232-258` 单视图分支禁止构造 `Window`。
- `MainView.axaml`（564 行）是桌面/浏览器共享的唯一外壳：侧边栏、页面宿主 `PageHost`、文件拖放遮罩 `FileDropOverlay`（482-518）、引导 `OnboardingGuide`（520-551）、对话框 overlay 宿主 `DialogOverlayHost`（553-562）。

### 2.2 侧边栏

- `MainView.axaml:29-42`：宽度绑定 `SidebarWidth`，`DoubleTransition` 0.25s；`ClipToBounds=True`。
- 展开态宽 140 / 折叠 64（`CLAUDE.md`）；窗口宽 < 750 自动折叠（`MainShellViewModel`）。
- 三段式 DockPanel：标题（Top，44px）→ 底部固定「设置/关于」（Bottom，`MainView.axaml:65-162`）→ 中部导航（填充，`MainView.axaml:165+`，含收起侧栏 + 7 个页面）。
- 展开/折叠两套并存的视觉树（`IsHitTestVisible` + `Opacity` 切换）—— 同一功能两份控件（如 81-99 与 119-145）。
- **已实锤缺陷 R-06**：窗口高度不足时（780×493 实测），中部导航列表溢出其分配区域，绘制在底部固定按钮之上，导致「历史快照」（y=429）与「设置」（y=409）重叠、命中区域互相干扰。证据：像素标定数据 + `assets/before` 之前的小视口截图；根因是中部 `Panel` 无滚动/裁剪策略（`MainView.axaml:165-180` 的 StackPanel + 固定 44px 行高 × 9 项超出可用高度）。

### 2.3 导航与页面解析

- `PageKey` 枚举 9 值（`Services/INavigationService.cs:7-18`）。
- `NavigationService` 用 `switch` 从 DI 解析（`Services/NavigationService.cs:46-73`）；`NavigateToAsync` 先执行 `CanLeaveAsync()`。
- 页面切换动画编排在 `MainShellViewModel.cs:77-79,100-147`：200ms 淡出 → 切换 → 100ms 间隔；引导/浏览器模式跳过。
- `ViewLocator.cs:25` 按 `ViewModel`→`View` 字符串替换反射创建 View。
- `Data/page_navigation.json` 可禁用页面（9 页当前全启用）。

## 3. 页面盘点

### 3.1 Home（`HomeView.axaml` 179 / `HomeViewModel.cs` 259）

- **区块**：头像+问候（22-55）；快捷链接 QUICKSTART/DOCS/FAQ（57-93）；RELEASE.md 渲染卡片（95-174）。
- **控件**：ScrollViewer + StackPanel + `ItemsControl`；Markdown 渲染 7 种 MdBlock 模板分支（Title/Heading/SubHeading/ListItem/Paragraph/Code/Empty）。
- **数据流**：嵌入资源 `Data/about.json`、`RELEASE.md`（csproj Embed 为 `Data.release.md`）；头像平台查找（`HomeViewModel.cs:165-241`，Windows AccountPictures / macOS dscl / Linux `.face`；失败回退首字母色块）。
- **交互**：`OpenUrlCommand`（108-129）；实现 `IFileDropHandler` 接受 `.seatsets`（247-258）。
- **生命周期**：`new Bitmap(avatarPath)` 从不释放（56）；`FilePathToBitmapConverter` 同样每次转换 new Bitmap（`Converters/ValueConverters.cs:64-74`）。

### 3.2 MemberManagement（`MemberManagementView.axaml` 754 / `MemberManagementViewModel.cs` 930 / code-behind 72）

- **区块**：工具栏（导出模板/导入/从文件更新/保存/另存为/卸载 + 紧凑 Flyout 折叠菜单 27-355；导出 CSV/Excel/JSON 239-353）；数据集列表（379-537，自绘折叠 + GridSplitter 540-544）；学生表（547-751）。
- **学生表实现**：`ScrollViewer`(664) > `ListBox`(665-730)，每行 = TextBox 姓名 + TextBox 身高 + ComboBox 性别 + CheckBox 前排 + 删除按钮（683-725）；底部「新增行」（603-662，Enter 提交，code-behind 47-54）。
- **交互**：点击数据集即加载（`SwitchToDatasetAsync`）；脏检查 = JSON 序列化快照对比（97-122）；切换/保存的 3 按钮对话框；导入 70×70 自动扫描阈值弹窗（365-480）；模板本地化 zh_cn/zh_tw/ja_jp/ko_kr（250-347）；`CanLeaveAsync` 未保存拦截（671-699）。
- **问题锚点**：`ScrollViewer>ListBox` → 虚拟化失效（官方文档明确，见 research/R1 §1）；每行 4 个可编辑控件；`_dialogLock` + `Task.Delay(150)` 防重入 11 处（235）；code-behind 像素级同步侧栏宽度（`MemberManagementView.axaml.cs:39-44`）。

### 3.3 VenueConfiguration（`VenueConfigurationView.axaml` 1228 / `VenueConfigurationViewModel.cs` 1231 / code-behind 61）

- **区块**：顶部删除/保存（15-50）；会场列表 + 新建/刷新/折叠（68-232）；布局类型 RadioButton Grid/Polar/Freeform（292-317）；Grid/Polar 参数面板（321+）；门 ItemsControl（约 990-1078）；Freeform 提示跳转（1083-1106）；预览画布（1108-1221）。
- **预览实现**：`ScrollViewer` + `CanvasZoomPan`(1134) → 两层 `ItemsControl`+`Canvas`（座位层 1140-1210、覆盖层含讲台/门/禁用座位，`RotateTransform` 1204-1206）。
- **数据流/状态**：约 40 个 `OnXxxChanged` partial 直接触发 `RegeneratePreview`（无防抖）；门集合事件订阅（1093-1117）；座位 ID 保留映射（36-40, 735-742, 778-784）；`NewVenue` 取消在途加载防竞态（284-304）；`InitializationTask` 供引导等待（249-256）。
- **问题锚点**：连续参数调整 → 预览全量重建（阶段 2 重点）。

### 3.4 FreeformManagement（`FreeformManagementView.axaml` 436 / `FreeformManagementViewModel.cs` 622 / code-behind 12）

- **区块**：顶部模板/CSV/JSON/保存（15-68）；已保存布局列表（87-142）；坐标表格（247-359，每行 5×NumericUpDown + 类型 ComboBox + 删除）；预览 Canvas 800×600（362-429，CanvasZoomPan 369-373）。
- **交互**：CSV/JSON 导入（213-349）、保存（387-431）、增删点、校验（501-528）、脏检查 JSON 快照（530-538）、`CanLeaveAsync`（540-568）、`_dialogLock` 9 处。
- **运行时观察**：首次进入触发 3 步页面引导（已实测：步骤 1 导入坐标 / 2 手动添加座位 / 3 保存布局）。
- **问题锚点**：`ScrollViewer>ListBox`（247-251）虚拟化失效；每行 6 控件。

### 3.5 StrategyConfiguration（`StrategyConfigurationView.axaml` 332 / `StrategyConfigurationViewModel.cs` 820 / code-behind 12）

- **区块**：侧栏策略列表（110-154，ToggleSwitch + 优先级 + 依赖子项竖线标识 + 配置失效提示 39-50 + 图例 + MoveUp/MoveDown/SaveAll 52-107）；右侧详情（158-330）：基本配置（235-272）、参数编辑器 `ContentControl`（274-281）、配置块 `ItemsControl`（284）、加载遮罩（314-329）。
- **交互**：独立/依赖策略展平与插入排序（224-305）；优先级冲突检测/自动修复/级联（397-630）；保存单个/全部、重置默认（638-789）；切换策略时自动保存旧策略脏配置块（187-211）；`CanLeaveAsync`（142-160）。
- **子组件**：`ConfigBlockEditorView`(108)+VM(637)、`ParameterEditorView`(36)+VM(119)、`StudentPickerView`(13)+VM(93)、`SeatPositionPickerView`(60)+VM(72)、`StrategyItemViewModel`(63)。
- **问题锚点**：`StudentPickerView.axaml:11` 与 `SeatPositionPickerView.axaml:13,18,29,34,46,52` 硬编码中文（未走 i18n）。

### 3.6 SeatingArrangement（`SeatingArrangementView.axaml` 819 / `SeatingArrangementViewModel.cs` 1219 / **code-behind 262**）

- **区块**：工具栏导出 MenuFlyout（16-150，学生/教师 × Excel/CSV/PDF/PNG 共 8 命令，Web 隐藏 PDF/PNG）；状态栏（153-181）与交换提示栏（184-203）；会场/数据集列表与生成按钮（227-308）；中央座位画布（327-486）。
- **画布实现**：两层 `ItemsControl`+`Canvas`（座位 375-414、覆盖层 415-445）+ `CanvasZoomPan` + `ZoomOnScroll`（365-373）；右下拖放垃圾桶（449-476）；模板使用 **`ReflectionBinding`**（383-395、423-425、430-441）。
- **交互（最重）**：Avalonia 12 `DataTransfer` 内部格式 `SeatFlow_Student`/`SeatFlow_Seat`（code-behind 28-32）；已占非固定座位可拖、未分配学生可拖、DragOver/Drop 校验、垃圾桶拖入（103-261）；拖动时 `Popup` 卡片跟随（53-99，**未 Dispose**）；点击/选中交换（VM 731-804）；拖放执行四情形（847-920）；撤销/重做 + 历史列表（922-1005）；保存快照（1009-1024）；导出 8 命令 + 30s 超时（1071-1173）；`CanLeaveAsync`（1028-1055）；缩放 0.2–3.0（91-93）。
- **生命周期**：`OnLoaded` 每次进入刷新（code-behind 20-24，订阅 `Loaded +=` 不退订 17）；构造函数订阅 `navigation.CurrentViewModelChanged` 永不退订（VM 176）。
- **问题锚点**：座位集合每次全量重建（`BuildSeatDisplayItems` 413-548）；ReflectionBinding；262 行拖放 code-behind；Popup 泄漏。

### 3.7 SnapshotHistory（`SnapshotHistoryView.axaml` 455 / `SnapshotHistoryViewModel.cs` 611 / code-behind 12）

- **区块**：顶部会场选择/刷新/创建/批量删除（26-93）；左侧列表普通/批量 CheckBox 两种模式（115-236）；右侧详情（244-452）含三类警告横幅（351-401：会场删除/会场变更/数据变更）与只读预览 `ItemsControl`+`Canvas`（404-447）。
- **交互**：预览构建（216-390，嵌入布局优先、`venueHash`/`studentHash`/学生 ID 完整性检测）；回滚（459-518，会场缺失时可恢复/导入）；删除/批量删除（520-596）；全选联动（598-602）。
- **注意**：**Transient** 注册（`Desktop/Program.cs:104`、`Browser/Program.cs:65`）→ 每次进入重建、页面状态不保留；静态构造器向共享 `JsonSerializerOptions` 注册 `SeatJsonConverter`（402-409）。

### 3.8 Settings（`SettingsView.axaml` 620 / `SettingsViewModel.cs` 762 / code-behind 12）

- **卡片区块**：外观（71-137，主题/默认缩放/语言）；存储（139-205，Web 隐藏：数据目录/打开/快照配额）；行为（207-248）；键盘快捷键 6 个 ToggleSwitch（250-367）；日志级别（369-399）；隐私/遥测/重开引导（401-450）；更新（452-557，Web 隐藏）；工具（559-615，`.seatsets` 导入导出）。
- **交互**：保存（180-366；语言切换重启 `#if !BROWSER` 346-351）；主题即时生效（248-263）；快捷键静态配置同步（285-296）；更新检查/下载进度（565-725）；`IFileDropHandler` 接 `.seatsets`（528-538）。
- **问题锚点**：快捷键通过静态变量下发（`Behaviors/KeyboardShortcutHandler.cs:20`）。

### 3.9 About（`AboutView.axaml` 347 / `AboutViewModel.cs` 148 / code-behind 12）

- **区块**：Hero（54-87）；4 链接卡片（89-152）；MIT 许可证横幅（154-170）；系统信息徽章（172-248）；依赖项 ItemsControl+WrapPanel（250-325）；页脚（327-342）。
- **数据**：`Data/about.json` 多语言；依赖版本来自编译生成的 `PackageVersions.Map`；版本 = `VersionInfo` + `GitCommit.Hash`。

## 4. 全局横切

### 4.1 对话框体系

| 类型 | 桌面 | 浏览器 |
|---|---|---|
| 确认/错误/警告/信息/多选项 | `DialogService` → `DialogWindow`（模态 `Window.ShowDialog`） | `WebDialogService` → `DialogOverlayHost` overlay + TCS |
| 单行输入 | `InputWindow` | `InputContent` |
| 更新 | `UpdateDialogWindow` | 不适用（WebNoopUpdateService） |

- 页面侧防重入：`_dialogLock` + `Interlocked.CompareExchange` + `Task.Delay(150)`，共 5 处（Member×11 次调用、Freeform×9、Seating 1073、Settings 77、FileService 15）。

### 4.2 Behaviors（5 个，全部静态类 + 附加属性风格）

| 文件 | 作用 | 风险 |
|---|---|---|
| `CanvasZoomPan.cs` (94) | 左键拖动平移 ScrollViewer；座位元素按下时 NaN 哨兵跳过 | 事件订阅仅 `SetEnabled(false)` 时退订；控件销毁不自动退订 |
| `ZoomOnScroll.cs` (43) | Ctrl+滚轮缩放 | 静态构造器注册全局 class handler |
| `ChineseInputNormalizer.cs` (108) | Tunnel 拦截 TextInput，全角→半角 | 全局常驻 |
| `FileDropHandler.cs` (231) | 全局 OS 文件拖放路由到当前页 `IFileDropHandler` + 遮罩显隐 | **静态 `_host` 强引用**；无 Detach |
| `KeyboardShortcutHandler.cs` (142) | 全局 Tunnel KeyDown：Ctrl+Z/Y/S、Delete、Esc；Ctrl+S 反射匹配 6 个保存命令 | **静态可变 `ShortcutConfig`** |

### 4.3 自定义控件

- 仅 1 个：`Controls/PhaseGuideIndicator.cs`（79 行，CodeWF Guide 的 `GuideIndicator` 子类，映射阶段进度 n/m）。**自定义控件策略问题不大——真正的维护成本在 Behaviors 与页面级重复逻辑。**

### 4.4 动画清单

**无关键帧动画**，全部为 `Transitions`：

| 位置 | 动画 | 评估 |
|---|---|---|
| `MainView.axaml:35-42` | 侧栏宽度 0.25s | 有语义（状态变化明显） |
| `MainView.axaml:72-79,124-131,173-180,342-349` | 侧栏内容 Opacity 0.25s | 与宽度过渡重复表达同一状态 |
| `MainView.axaml:469-476` + `MainShellViewModel.cs:77-147` | 页面切换淡出 200ms + 100ms 间隔 | 每次导航强制延迟 ~300ms，疑似「响应慢」体感来源 |
| `MemberManagementView.axaml` 11 处 | 工具栏折叠/透明度 0.1–0.25s | 多数可删 |
| `VenueConfigurationView.axaml:76-184` | 列表折叠/透明度 | 同上 |
| `Styles/Controls.axaml:10-16,94-100,131-137` | hover BrushTransition 0.12s | 保留（有反馈价值） |
| `Styles/Guide.axaml:159-172` | 引导卡片缩放 0.15s | 保留 |
| `OnboardingService.AnimateCardBounceAsync`（799-812） | 卡片弹跳 | **死代码**（无调用点） |

### 4.5 生命周期与静态状态风险

- **静态可变状态**：`ViewModelBase._logger` + `static IDialogService Dialog`（13-16）；`KeyboardShortcutHandler.ShortcutConfig`；`FileDropHandler._host`；`WatchdogService._dialog`；`OnboardingService` 4 个静态字段（51,57-59）；`SeatDisplayItem` 12 个静态画刷（71-82）；`App.axaml.cs` 4 个静态字段。
- **fire-and-forget 构造器初始化**：8 个 VM 在构造函数发起异步加载（Member 219、Venue 256、Strategy 137、Snapshot 127、Seating 177、Settings 174、Freeform 79、ConfigBlock 63）——竞态与「进入即闪」的根源，引导系统因此依赖 `InitializationTask`。
- **订阅泄漏点**：`SeatingArrangementViewModel:176`（导航事件）、`MainShellViewModel:91`、`MainWindow.axaml.cs:30-31`、`App.axaml.cs:199-221`、`UpdateDialogWindow.axaml.cs:46,64,71`（重复注册）、`SeatingArrangementView.axaml.cs:17`（Loaded）。
- **IDisposable 未释放**：`WatchdogService`、`ArrangementCounterService`、`TelemetryService/HttpClient`（退出仅 Flush）；`Bitmap`（Home/Converter）；`Popup`（座位拖拽）。
- **生命周期策略不一致**：SnapshotHistory 为 Transient，其余 8 页 Singleton（`Desktop/Program.cs`、`Browser/Program.cs`）。

### 4.6 重复模式（跨页横切缺口）

| 模式 | 出现 | 现状 |
|---|---|---|
| 脏检查 | 5 种实现（JSON 快照 ×2、聚合属性、手写标记、IsDirty 组合） | 无统一抽象 |
| `CanLeaveAsync` + Save/Discard/Cancel | 5 页各写一遍 | 可抽基类/服务 |
| `_dialogLock` 防重入 | 5 处 | 可抽 `IDialogGate` |
| 侧栏折叠 | 3 份状态实现 + 2 份 code-behind 像素同步 | 可抽控件/行为 |
| 会场/数据集列表加载 | 4 个 VM 重复同样循环 | 可抽加载服务 |
| 导出流程（互锁+SaveFile+超时+清理） | Member 与 Seating 各一套 | 可抽导出服务 |
| IsLoading + 遮罩 | 3+ 页各自实现 | 统一 Busy 状态 |
| Home/About 的 OpenUrl | 2 份几乎相同 | 共用服务 |

### 4.7 引导系统（最大隐性耦合）

- `OnboardingService`（846 行）同时实现 `IOnboardingService` + `IOnboardingStarter`；启动引导 **9 个阶段 / 24 步**（运行时日志实证；注意 `CLAUDE.md` 记载的「v3.2 / 20 步」已过时，实际配置为 v3.4）。
- 直接操作各页 VM 的公开属性（`OnboardingService.cs:396-602`），并通过 4 个静态字段保存/恢复演示数据。
- 目标控件依赖 `x:Name`（NameScope 查找）；`HandleStepOpening` 必须先导航再解析目标（Phase 1 修复经验）。
- 页面引导：FreeformManagement 3 步（本次实测通过）。
- 首次启动还有**遥测同意弹窗**（`App.axaml.cs:520-544`，`Telemetry.ConsentShown`）。

### 4.8 i18n / 主题 / 平台差异

- i18n：`.resx` 3 文件 + `scripts/i18n.py` 维护；`{x:Static}` 属性语法；已知 2 个 View 硬编码中文（§3.5）。
- 主题：App.axaml 双主题字典（侧栏/语义色/表面/阴影/遮罩）+ 3 个 StyleInclude；画刷经 `DynamicResource`。
- 平台差异：Web 隐藏 PDF/图片导出、自动更新、存储/更新设置卡；Web 对话框为 overlay；换页动画 Web 禁用；WASM 单线程、嵌入 CJK 字体、反射序列化开启。

## 5. 新发现问题汇总（阶段 1 实测 + 代码审阅）

| # | 问题 | 证据 | 严重度 |
|---|---|---|---|
| I-01 | 小高度视口下侧栏中部导航溢出并覆盖底部「设置/关于」 | 像素标定：780×493 时 历史快照 y=429 vs 设置 y=409 重叠；`MainView.axaml:165-180` | 高 |
| I-02 | 页面切换强制 300ms 动画延迟（200ms 淡出 + 100ms 间隔） | `MainShellViewModel.cs:77-147` | 中 |
| I-03 | 座位画布每座位一个控件 + ReflectionBinding + 全量重建 | `SeatingArrangementView.axaml:375-445`；VM 413-548 | 高 |
| I-04 | 会场参数每次变更全量重建预览，无防抖 | `VenueConfigurationViewModel.cs:1051-1089` | 高 |
| I-05 | 名单/自由布局列表虚拟化被 `ScrollViewer>ListBox` 禁用 | `MemberManagementView.axaml:664-665`、`FreeformManagementView.axaml:247-251` | 高 |
| I-06 | 引导 X 按钮在小视口外命中困难 / 模态阻断（实测两次才命中） | 运行时观察 + 像素图 | 低 |
| I-07 | 「无意义动画」清单（侧栏内容 Opacity 与宽度重复、工具栏折叠动画、死代码弹跳） | §4.4 | 中 |
| I-08 | 5 套脏检查/5 份 CanLeaveAsync/5 处 dialogLock 等重复模式 | §4.6 | 高（维护成本） |
| I-09 | 静态可变状态 + 订阅泄漏 + IDisposable 未释放 + 构造器 fire-and-forget | §4.5 | 高 |
| I-10 | SnapshotHistory Transient 与其他 Singleton 不一致 | `Desktop/Program.cs:104`、`Browser/Program.cs:65` | 中 |
| I-11 | 两处 View 硬编码中文未走 i18n | `StudentPickerView.axaml:11` 等 | 低 |
| I-12 | `CLAUDE.md` 引导系统描述过时（v3.2/20 步 vs 实际 v3.4/9 阶段 24 步） | 运行时日志 | 低（文档） |

## 6. 盘点的完整性与后续

- 本文档为**功能与实现现状**盘点；性能根因量化、证据化排序在阶段 2（`02-performance-ux-diagnosis.md`）完成。
- 基线截图：`assets/before/01-home.png` … `09-about.png`（1200×800，zh-CN，浅色，空数据状态）。
- 阶段 2 需要补充的采样场景：注入演示数据（名单 ≥200 人、会场 ≥60 座、含快照）后，对 I-03/I-04/I-05/I-02 做 CDP trace + 堆快照。
