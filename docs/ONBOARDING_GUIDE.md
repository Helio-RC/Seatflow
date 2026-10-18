# OOBE 首次设置设计文档

> 版本 4.0 — MainView 覆盖层 + 真实数据 + 生成交接。决策见 [ADR-016](adr/ADR-016-oobe-replaces-codewf-guide.md)。

## 概览

SeatFlow 首次启动不再使用 CodeWF `Guide` 与 JSON Tour，而是打开 `MainView` 内的**全屏 OOBE 覆盖层**：

1. **欢迎 + 遥测**：三步课桌式导览（中间留过道）、匿名使用数据复选框（默认勾选）。
2. **启动偏好**（仅桌面）：自动更新模式三选一。
3. **名单**：导入 xlsx / 从空白表格开始 / 沿用已有数据集；表格与人员管理页共用 `StudentRosterTable`，支持行内编辑，已有数据集可改并原地更新。
4. **会场**：行列、过道、每列行数、名称；控件与会场管理页一致，预览改为编辑区下方的全宽卡片，支持平移/缩放。
5. **生成交接**：关闭全屏层，导航排座工作台并预选会场/数据集；`CoachMarkOverlay` 高亮生成按钮。

OOBE 不是 `PageKey`，不进入 `NavigationService` 的页面缓存，也不参与侧栏导航。

## 关键文件

| 文件 | 职责 |
|---|---|
| `Services/IOobeService.cs` | 启动/完成/激活态契约 |
| `ViewModels/OobeViewModel.cs` | 唯一状态机、真实数据写入、交接准备 |
| `ViewModels/IStudentRosterEditor.cs` | 名单表格编辑契约（人员管理页与 OOBE 共用） |
| `Controls/StudentRosterTable.axaml(.cs)` | 共享名单表格：表头 / 虚拟化列表 / 行内编辑 / 新增行 |
| `Views/OobeView.axaml(.cs)` | 欢迎/偏好/名单/会场四屏 |
| `Controls/CoachMarkOverlay.axaml(.cs)` | 自绘遮罩 + 目标描边 + 非模态卡片 |
| `Services/StartupRestoreState.cs` | `.seatsets` 自动导入结果 |
| `Services/FirstLaunchPolicy.cs` | 首启判定纯函数 |
| `Services/VenueGridPreviewBuilder.cs` | 与会场页一致的网格预览几何 |
| `Services/VenueLayoutDefaults.cs` | OOBE 与会场页共用「新建会场」默认参数（间距/桌面/讲台） |
| `App.axaml.cs` | 首启检测/落盘、OOBE 启动、更新顺序 |
| `Behaviors/KeyboardShortcutHandler.cs` / `FileDropHandler.cs` | OOBE 活跃时短路 |

## 首启与重跑

```text
App.InitializeAsync
  ├── 采样 AppSettings 是否存在（环境警告写盘前）
  ├── RunStartupChecksAsync()（单实例 / 环境警告）
  ├── CheckSeatSetsAutoImportAsync() → StartupRestoreState
  ├── DetectFirstLaunchAsync()
  │     ├── 启动前采样的 settings 是否存在 / IsFirstLaunch / SeatSetsImported
  │     ├── 命中 → IsFirstLaunch=true 落盘
  │     └── 未命中 → false
  ├── RestoreSettingsAsync()
  └── 首启 → Oobe.StartAsync()
        非首启 → 记录启动遥测 + ScheduleAutoUpdateCheck()
```

- 首启判定使用环境检查**之前**采样的文件存在性；环境警告“不再提醒”会创建 `AppSettings.json`，不能据此把真实首启误判为非首启。

- 崩溃/关窗在 OOBE 中途：`IsFirstLaunch` 仍为 true，下次重新进入。
- 设置页「重新开始首次设置」：写 `IsFirstLaunch=true` 后 `StartAsync(isRestart:true)`，从名单步骤开始，不重复询问遥测。
- 首次安装自动导入 `.seatsets`：即使数据包包含 AppSettings，也进入 OOBE，并显示恢复结果。

## 数据与保存

- **名单**：上传仅 `.xlsx`；首次保存用 `SaveStudentDatasetAsync`，回退再改走 `UpdateStudentDatasetAsync`，避免重复数据集。已有数据集可编辑：继续时仅有改动才按原 ID 原地更新；`OOBE` 保留导入顺序，不按姓名重排，空白名单从 0 行开始并用底部新增行输入。
- **会场**：新建写 `GridLayoutBuilder.BuildGrid` + `SaveVenueAsync`；默认参数统一由 `VenueLayoutDefaults` 提供（横/纵间距 60/45、桌内/桌间 40/32、过道 60、讲台 100×40），与会场页「新建会场」完全一致；新建 ID 为 8 位、名称为 `新会场_<id>`；进入会场步骤默认新建，不自动选中上次会场，已有会场需在列表中手动载入。已有网格会场只改四项，其余元数据保留，并按 `(Row,Column)` 复用座位 ID；非网格会场只读沿用。会场控件与会场页共用 `sf-chip` 过道、`sf-field`/`sf-hint` 标签与同一组范围/tooltip；预览为编辑区下方的全宽卡片，支持左键拖空白平移、滚轮缩放。
- **完成标记**：`CompleteAsync` 写 `IsFirstLaunch=false`；首次生成成功也会立即写 false。
- **保存期间锁定**：`IsBusy` 时步骤内容 `IsEnabled=false`，避免慢盘保存期间继续编辑导致未保存修改被交接丢弃；会场保存后若编辑版本变化，停留会场并提示重新保存。
- **交接**：`SeatingArrangementViewModel.PrepareOobeHandoffAsync` 等待 `InitializationTask`（10s 超时）、绕过 `_dataLoaded` 重新加载列表、显式 `LoadVenueAsync`，再设置选中项。交接准备结束后按初始化任务状态解除覆盖保护，后续导入 `.seatsets` 仍会恢复工作区。生成缺数据结果携带 `SeatingMissingData`（会场/名单/两者），交接卡据此精确提示并跳转到对应步骤；空名单也归入缺名单而不是失败卡。

## 遥测与更新

- `TelemetryConfig.Enabled` 默认 true；实际启用 `Enabled && ConsentShown`。
- 欢迎屏默认勾选；点“继续”按当前勾选写入。点“跳过设置”按关闭处理（`ConsentShown=true`、`Enabled=false`），不会因默认勾选而静默启用遥测。
- OOBE 保存设置后调用 `ITelemetryService.ApplyEnabled`（不二次落盘）；设置页仍用 `SetEnabled`。
- OOBE 活跃（含交接）期间不弹更新；`Oobe.Completed` 后 `ScheduleAutoUpdateCheck`。

## 层叠与命中

| 层 | ZIndex |
|---|---|
| `FileDropOverlay` | 100 |
| `OobeView` | 200 |
| `CoachMarkOverlay` | 210 |
| `DialogOverlayHost` | 300 |

全屏 OOBE 时 `MainView` 的 `DockPanel IsEnabled=false`；交接时恢复。Coach mark 根容器无背景、遮罩 `IsHitTestVisible=false`，只有卡片可命中，目标按钮保持可点击。

## 测试

- `OobeViewModelTests`：步骤推进、遥测单次落盘与“跳过设置”关闭遥测、名单保存/沿用、会场保存期间修改拦截、完成标记。
- `FirstLaunchPolicyTests` / `StartupRestoreStateTests` / `StartupSettingsTests`：首启判定、`.seatsets` 恢复状态、环境警告写盘不回滚首启字段。
- `OobeHandoffPreparationTests` / `VenueGridPreviewBuilderTests`：交接准备与会场页几何一致。
- `OobeViewLayoutTests`：三档布局、保存期间步骤内容禁用。
- Presentation Headless 测试覆盖 MainView 三档布局与共享控件。
- 手工：临时 `DataDirectory` 走首启/重跑/崩溃恢复/`.seatsets`/非网格会场/失败重试；桌面、WASM、360×480、明暗、中英文。

## 视觉规范

沿用方向 B「方格纸」令牌（`Resources/Tokens/`）与 [Design_Spec](presentation/Design_Spec.md)；不提交截图或独立样机。
