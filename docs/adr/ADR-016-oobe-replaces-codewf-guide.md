# ADR-016：OOBE 覆盖层取代 CodeWF Guide

## Status

Accepted

## Date

2026-10-17

## Context

旧首次引导由 `CodeWF.AvaloniaControls` 的 `Guide` 控件 + `onboarding_config.json`（24 步）驱动，存在以下问题：

- 引导步骤依赖演示数据注入（ADR-008），不产生真实数据集/会场，完成后用户仍面对空工作区。
- Guide 使用独立 Popup 层，窗口最小化/切换时会出现孤儿窗口；步骤目标解析依赖 NameScope 顺序。
- 首次启动时先弹遥测同意对话框，再启动引导，两条流程互相抢占。
- 引导完成标记在检测阶段立即写 `IsFirstLaunch=false`，崩溃后无法恢复。
- 会场配置只能通过高亮现有页面完成，首次用户必须理解整页会场编辑器。

## Decision

**移除 CodeWF 依赖与旧 Tour，改为 MainView 内的全屏 OOBE 覆盖层。**

1. **状态机**：`OobeViewModel`（`IOobeService`）负责欢迎/遥测 → 启动偏好（桌面）→ 名单 → 会场 → 交接；`OobeView` 直接挂在 `MainView`，不新增 `PageKey`。
2. **真实数据**：名单写入 `SaveStudentDatasetAsync` / `UpdateStudentDatasetAsync`，会场写入 `SaveVenueAsync`；完成后直接得到可用工作区。
3. **首启标记**：真实首启、设置页重跑、或本次自动导入 `.seatsets` 时，`IsFirstLaunch` 落盘为 `true`；仅在完成/跳过/首次生成成功时置 `false`。
4. **遥测**：欢迎屏复选框默认勾选；点“继续”前不上报；点“跳过设置”按关闭处理（写入 `ConsentShown=true`、`Enabled=false`），不按默认勾选静默启用；实际启用条件为 `Enabled && ConsentShown`；OOBE 只写一次 AppSettings，随后 `ApplyTelemetryEnabled` 同步内存状态。
5. **更新检查**：OOBE 活跃期间不检查更新；`Oobe.Completed` 后调度。
6. **会场四项**：只复用网格预览 + 行列 + 过道 + 每列数量；已有网格会场保留其他元数据并按 `(Row,Column)` 复用座位 ID；非网格会场只读沿用。
7. **交接**：关闭全屏层，导航到排座工作台并预选会场/数据集；自绘 `CoachMarkOverlay` 高亮生成按钮；成功/失败/缺数据分别展示完成/失败/补全卡。
8. **视觉规范**：沿用方向 B「方格纸」令牌与 `Design_Spec.md`，不提交截图或独立样机。

## Consequences

- 删除 CodeWF 包、Guide 控件/主题、`onboarding_config.json`、`OnboardingService`、`IGuideSeedTarget` 及对应资源与测试。
- 首次启动不再有独立遥测对话框；OOBE 首屏负责同意。
- `StartupRestoreState` 向 OOBE 暴露 `.seatsets` 恢复结果。
- 旧 ADR-008 的演示数据注入方案被取代。
- OOBE 与页面共享 `SeatingArrangementViewModel`/`SeatingCanvas`/`VenueGridPreviewBuilder`，减少重复几何逻辑。
