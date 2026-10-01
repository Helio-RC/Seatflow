# 09 · M3 交接说明（排座工作台 + 新外壳 IA）

> 用途：把 M3 交给新的开发会话/子代理实施。当前分支 `refactor/ui-overhaul` 工作树干净，
> HEAD = `fd0542c`（M2 完成）。先读本文件，再按 `06-refactor-plan.md` §M3 执行。

## 1. 当前进度（已验收，可直接开始 M3)

| 里程碑 | 提交 | 内容 |
|---|---|---|
| 文档基线 | `b37dabb`（tag `ui-refactor-baseline`） | 阶段 0–6 调研/设计/计划 + 实施日志 |
| M0 基座 | `4ea146f` | Avalonia 12.1.3 + ReactiveUI.Avalonia 12.1.5；方向 B 令牌/组件样式；横切服务（IPageLifecycle/IDialogGate/IBusyScope/DirtyTracker）；静态可变状态清除 |
| M1 画布 | `9a3ccaa` | `Controls/SeatingCanvas`（合批渲染/命中/拖拽/缩放/键盘/手势 LOD/文本几何缓存）；接入排座页；spike #2 达标（300 座稳态 3.6–8.7ms/帧、最长任务 84ms） |
| M2 会场页 | `fd0542c` | 三布局合并（自由点并入）、120ms 去抖 + 过道选项增量同步、四布局预览走画布、IPageLifecycle/DirtyTracker；行数 ×15 INP 315→79ms |
| 资产政策 | `850be16` | **图片/trace 不入库**（`.gitignore` 含 `docs/ui-refactor/assets/`），只提交 md 与代码 |

细节与证据索引见 `08-implementation-log.md`；性能口径见 `02-performance-ux-diagnosis.md` §6。

## 2. M3 范围（06 §M3，逐条）

1. **排座工作台三栏**：左「数据选择」（会场/数据集）→ 中 `SeatingCanvas` → 右「检查器」用 `TabControl` 页签（策略/未分配/记录/消息），替代现有右栏纵向堆叠。
2. **命令栏**：导出菜单（学生/教师 × Excel/CSV/PDF/PNG；Web 隐藏 PDF/PNG）、保存快照、撤销/重做（沿用 `CommandHistory`）、生成/创建空白；沿用现有 30s 超时与平台差异。
3. **切页即时化**：删除 `MainShellViewModel` 的 200ms 淡出 + 100ms 间隔（当前实现：`FadeOutDuration` 于 `ViewModels/MainShellViewModel.cs:77`，`RunTransitionAsync` 于 :101-140）；允许 ≤120ms 非阻塞淡入；浏览器模式保持跳过。
4. **新外壳 IA**：
   - 侧栏分组（工作流/资料/规则/记录 + 底部设置/关于），中部导航**可滚动 + 最小高度保护**（修复 I-01：矮视口导航覆盖底部按钮）；
   - ≤900px 移动断点：导航左抽屉、上下文右抽屉（遮罩、切换页面自动收起），视觉与交互基准 `html-sample/src/app.css` 的 `.compactbar/.drawer-mask/.rail/.picker/.inspector` 与 `App.jsx`。
5. **Home 移除**：默认入口=排座工作台；欢迎/RELEASE 内容压缩进工作台空态卡片。需要同步的位置（已确认）：
   - `Services/NavigationService.cs:23`（初始导航 `PageKey.Home`）、`:51`（switch 解析 `HomeViewModel`）；
   - `App.axaml.cs:730`（`NavigateTo(PageKey.Home)`，`.seatsets` 导入后回首页）；
   - `Services/OnboardingService.cs:106, 266, 781`（Home 导航；`? : PageKey.Home` 兜底）；
   - `ViewModels/HomeViewModel.cs`、`Views/HomeView.axaml(.cs)`（删除）；`Data/page_navigation.json`、Desktop/Browser `Program.cs` 注册；
   - `SeatSetsImportHelper` 的 `HomeViewModel` 复用注释/调用若存在一并调整。
   - 引导（`onboarding_config.json` 的启动步骤）若含 Home 目标，做**最小重映射**到工作台/对应页面；完整引导重写在 M5，但不得让引导崩溃（可先跑通首启流程）。
6. **生命周期**：`SeatingArrangementViewModel` 实现 `IPageLifecycle`（`OnEnterAsync`/`OnLeaveAsync`），去掉构造器 fire-and-forget 加载（`View Loaded/Unloaded` 桥接）；保留 `InitializationTask`（引导演示数据依赖它）。
7. **编译绑定**：清除排座页/Home 相关的全部 `ReflectionBinding`。当前仅剩 `Views/SnapshotHistoryView.axaml:420-421`（属 M4，不要顺手改）。

## 3. 可复用资产与模式（不要重造）

- 画布：`Controls/SeatingCanvas.cs` + `Controls/SeatVisual.cs`
  - 属性：`Snapshot`（不可变 `SeatLayoutSnapshot`，含 `Seats`/`Overlays`/`BoardWidth/Height/Version`）、`SelectedSeatId`、`Zoom`、`PanOffset`；
  - 方法：`HitTestSeat(Point)`、`SetDropTarget(string?)`、`ZoomAt`、`FitToView`、`PanBy`；`DiagnosticsEnabled` 性能日志开关（默认 false，采样时可临时置 true）；
  - 事件：`SeatClicked` / `SeatActivated` / `SeatDropped(source,target,studentId,pointer)`；
  - 内部拖拽（座位↔座位/垃圾桶）已在控件内完成；外部拖放（未分配学生→座位）走宿主 DragDrop + `HitTestSeat`。
- 令牌/样式：`Resources/Tokens/{Colors,Spacing,Typography}.axaml`、`Tokens/MotionTokens.cs`、`Styles/Components.axaml` 的 `sf-*` 类（卡片/按钮/输入/页签/徽标/抽屉相关类可继续扩）。
- 横切：`Services/{IPageLifecycle,IDialogGate,IBusyScope,DirtyTracker,NullDialogService}.cs`；VM 基类构造注入 `ViewModelBase(IDialogService, ILogger?)`。
- 去抖范式（M2 已验证）：单一 `OnPropertyChanged` 入口 + `PreviewRevision++` + `this.WhenAnyValue(...).Throttle(120ms, RxSchedulers.MainThreadScheduler).ObserveOn(...).SubscribeSafe(...)`；避免逐属性重建控件（尤其 `ItemsControl` 整表替换）。
- 现有排座页交互契约（保留语义）：`Views/SeatingArrangementView.axaml.cs`（`DragFormats` / 外部拖放 / 垃圾桶几何判定）与 VM 的 `ClickSeatCommand`、`ExecuteDropAsync(studentId, sourceSeatId, targetSeatId)`、`ExecuteRemoveToTrashAsync`、`Undo/Redo`、`SaveToSnapshotCommand`、`CancelSwapCommand`、`HistoryEntries/ActiveStrategies/UnassignedStudents/Messages`。

## 4. 环境与验证（无头）

- .NET 10.0.401；wasm-tools 已装；Avalonia 12.1.3；ReactiveUI.Avalonia 12.1.5。
- 浏览器 CDP 端点 `localhost:3000`，**每隔几分钟会被重置**（页面回 about:blank，IndexedDB 可能清空）。
  - 输入必须走 `docs/ui-refactor/tools/cdp.mjs`（canvas 无 DOM，MCP 的 uid 点击不可用）；该脚本已支持：`nav <url>`（无页面时自动建页）、`size <w> <h>`、`click/key/eval/shot`。
  - 确定性会话：`dotnet run docs/ui-refactor/tools/seed-demo-data.cs` → `node docs/ui-refactor/tools/seed-demo-data.mjs`（注入 5 个演示会场 + 240 人名单 + 免弹窗设置，并 reload）。
  - 整轮交互请放在**同一条 bash 命令**内执行，缩小重置窗口；截图/console/trace 用 chrome-devtools MCP。
- 发布：`dotnet publish src/SeatFlow.Browser -c Release -p:WasmBuildNative=true -o /tmp/seatflow-web`，本地以 8090 静态服务。
- **不要提交图片/trace**；`docs/ui-refactor/assets/` 已在 .gitignore。
- 若用子代理实施：明确禁止其执行 publish/起服务/访问 /tmp（会触发 Codeg 权限弹窗阻塞），这些由主会话统一做；子代理只做 build + test。

## 5. 验收（主会话执行）

1. `dotnet build`（双 TFM）0 警告 0 错误；`dotnet test` 381 全绿。
2. WASM 实机（1200×800 + 780×493 两档）：
   - 切页 INP ≤100ms、无 >100ms 长任务（trace 口径同 02）；
   - 工作台三栏 + 右栏页签、导出菜单、快照保存、撤销/重做；
   - ≤900px 移动断点：导航左抽屉 + 上下文右抽屉 + 遮罩关闭；
   - 矮视口（780×493）侧栏不覆盖底部「设置/关于」（I-01 修复）。
3. 引导最小可用：首启流程不崩（完整回归在 M5/M6）。
4. 更新 `08-implementation-log.md`（M3 段落 + 证据 + 偏差），提交：
   `refactor(ui): M3 排座工作台 + 新外壳 IA（Home 移除/页签/抽屉/即时切页）`。

## 6. 已知风险/注意

- `OnboardingService` 直接调用页面 VM 的公开成员（见 08 日志 M2 段核对表）；改 VM 签名前先 `grep` 该服务并逐条保留/最小同步。
- 页签化后右栏原有滚动/折叠状态（`IsMessagesExpanded` 等）要迁移为页签内布局，避免丢失消息可见性。
- 旧排座页 code-behind 的 Popup 拖拽卡片已在 M1 删除，不要恢复。
- 移动抽屉注意与自绘画布的指针事件冲突（画布空白拖拽=平移，抽屉开合用按钮）。
