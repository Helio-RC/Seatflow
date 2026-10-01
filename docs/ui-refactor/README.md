# SeatFlow UI 彻底重构 — 文档索引

> 状态：**全部阶段交付完成 —— 等待终审（门 6）**
> 开始日期：2026-10-01 · 工作树：`.worktrees/ui-refactor`（分支 `refactor/ui-overhaul`）

本目录是「UI 彻底重构」工作流的唯一事实来源。所有结论、证据、决策与计划均落文档，不以对话为准。

## 工作流与审核门

| 阶段 | 交付物 | 状态 | 审核门 |
|---|---|---|---|
| 0 调研与工具链 | `00-charter-and-decisions.md`、`research/`、`tools/` | ✅ 完成 | — |
| 1 当前 UI 功能调查 | [`01-current-ui-inventory.md`](01-current-ui-inventory.md)、`assets/before/` | ✅ 完成 | ✅ 门 1 通过（2026-10-01） |
| 2 性能与体验诊断 | [`02-performance-ux-diagnosis.md`](02-performance-ux-diagnosis.md) | ✅ 完成 | ✅ 门 2 通过（2026-10-01） |
| 3 全新 UI 设计 | [`03-ui-redesign.md`](03-ui-redesign.md) + `direction-mocks/` | ✅ 完成 | ✅ 强制门 3 通过（方向 B + IA 提案） |
| 4 HTML 样机 | [`html-sample/`](html-sample/) + [`04-html-sample.md`](04-html-sample.md) | ✅ 完成（含移动端/大教室/三布局预览修正） | ✅ 门 4 通过 |
| 5 技术选型 | [`05-tech-selection.md`](05-tech-selection.md) + `spikes/` | ✅ 完成：**B · ReactiveUI.Avalonia**（spike 通过：+1.24MB / +10% 启动） | 门 5 待确认 |
| 6 完整重构计划 | [`06-refactor-plan.md`](06-refactor-plan.md) | ✅ 完成 | **门 6（终审，当前）** |

> 审核门规则：每阶段先产出文档 → 摘要 + 关键决策点 → **等用户明确说「审核通过」才进入下一阶段**。
> 阶段 3、5 为强制门，不得绕过。实施不在本轮范围，待阶段 6 计划批准后另启动。

## 目录结构

```
docs/ui-refactor/
├── README.md                      # 本文件：索引 / 状态 / 审核门 / 环境
├── 00-charter-and-decisions.md    # 范围、决策记录、假设、风险
├── 01-current-ui-inventory.md     # 阶段 1：UI 功能全量盘点
├── 02-performance-ux-diagnosis.md # 阶段 2：性能与体验诊断（含 trace 证据）
├── 03-ui-redesign.md              # 阶段 3：新 UI 设计（方向 B 已选定）
├── direction-mocks/               # 阶段 3：三套方向对比的最小 HTML 稿
├── 04-html-sample.md              # 阶段 4：样机说明
├── html-sample/                   # 阶段 4：React/Vite 高保真原型
├── 05-tech-selection.md           # 阶段 5：技术选型
├── 06-refactor-plan.md            # 阶段 6：完整重构计划
├── spikes/                        # 技术验证工程（编译/运行时 Spike）
├── research/                      # 阶段 0/2/5 调研笔记
│   └── R1-avalonia12-performance.md
├── assets/                        # 基线截图 / design-directions / html-sample / traces
└── tools/                         # WASM 查看/交互/采样工具（见 tools/README.md）
```

## 环境与复现（无头）

- .NET SDK 10.0.401；Avalonia **12.1.2**（禁止降级）；node 24 / pnpm 11。
- 界面验证链路：`dotnet publish src/SeatFlow.Browser -c Release` → 本地静态服务（:8090）→ 内网 Chromium（chrome-devtools MCP，`browserUrl=http://localhost:3000`）。
- 基线：`dotnet build` 通过；`dotnet test` **381 通过 / 0 失败**（2026-10-01，worktree）。
- 自动化：`docs/ui-refactor/tools/`（CDP 可信输入 + 像素定位）；MCP 负责查看、截图、控制台与性能 trace。

## 决策日志（详见 00 章）

| # | 决策 | 结论 |
|---|---|---|
| D1 | 改动范围 | 允许较大范围重写（含引导/遥测子系统），可新增 UI 测试项目 |
| D2 | 功能与 IA | 功能全保留；IA 可重新设计；引导随新 IA 重写 |
| D3 | 平台目标 | 桌面 / WASM 同等重要，分别验收 |
| D4 | 性能焦点 | 座位画布、会场实时预览、页面切换/启动/WASM 整体 |
| D5 | 视觉方向 | 阶段 3 出 2–3 套方向对比（文字 + 令牌 + 每方向 1 个代表页最小 HTML）后定 |
| D6 | 技术底线 | 允许评估并更换 MVVM 框架、按需引入控件/主题库（阶段 5 出证据化结论） |
| D7 | HTML 样机 | React/Vite 高保真可交互原型，本地服务 + MCP 查看 |
| D8 | 性能验收 | 硬指标 + 前后对比（数值基于阶段 2 WASM 实测基线） |
| D9 | 实施边界 | 本轮止于阶段 6 计划 |
| D10 | 兼容性 | 用户数据/设置/引导进度全部向后兼容，必要时加迁移器 |
| D11 | 双端功能 | 逐项重审平台差异，允许 Web 用浏览器能力替代（如打印替代 PDF） |
| D12 | 提交策略 | 默认不提交 git；由用户决定提交/合并时机 |

## 风险清单（滚动更新）

| # | 风险 | 等级 | 应对 | 状态 |
|---|---|---|---|---|
| R-01 | CDP 端点重连后浏览器可能被重置（页面/IndexedDB 清空） | 中 | 自动化脚本幂等化（先 nav + 等待）；演示数据一键重建（`tools/seed-demo-data.*`） | 已缓解 |
| R-02 | ReactiveUI 在 WASM 裁剪/AOT 下兼容性未知 | 中 | 阶段 5 spike 验证后再推荐 | 待处理 |
| R-03 | 引导系统（846 行、直接操作页面 VM）重写回归风险高 | 高 | 单独立项、行为清单先行、纳入测试策略 | 待处理 |
| R-04 | 无头环境无法直接验证桌面端卡顿 | 中 | VM 级基准 + 代码证据 + WASM 实测交叉推断，标注置信度（02 §5） | 已缓解（有置信度分级） |
| R-05 | 空数据无法覆盖真实负载 | 中 | ✅ 已解决：演示数据 64 座/240 人已注入并用于全部采样 | 已关闭 |
| R-06 | 屏幕小高度下侧栏导航溢出覆盖底部按钮（已实锤） | 中 | 纳入 02 诊断与 03 设计约束（信息架构需处理窄/矮视口） | 已记录 |
| R-07 | 无头软件 WebGL 使 Paint/Commit 数字偏悲观 | 低 | 02 已声明口径；阶段 6 计划增加真实 GPU 复核项 | 已记录 |
