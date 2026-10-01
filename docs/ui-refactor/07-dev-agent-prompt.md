# 交给开发 Agent 的启动提示词（Handoff Prompt）

> 用途：新建一个开发 agent 会话，粘贴下面代码块中的内容即可开始实施（本轮先做 M0，验证后停下汇报）。

```text
你是 SeatFlow 项目的开发 agent。请在本仓库的 worktree `.worktrees/ui-refactor`（分支 `refactor/ui-overhaul`）中，按已批准的重构计划实施 UI 彻底重构。

【第一步：先读文档，再动代码】
按顺序阅读：
1. docs/ui-refactor/README.md            —— 总索引、审核门、决策日志、环境与复现
2. docs/ui-refactor/06-refactor-plan.md  —— 你要执行的计划（里程碑 M0–M6、验收 M1–M6、DoD、风险回滚）
3. docs/ui-refactor/05-tech-selection.md —— 技术选型（B 路线：ReactiveUI.Avalonia；绑定/画布/测试决策）
4. docs/ui-refactor/03-ui-redesign.md    —— 新 IA、方向 B 设计令牌、动效政策、生命周期框架
5. docs/ui-refactor/04-html-sample.md 与 html-sample/ —— 视觉与交互基准（React 原型，可运行参考）
6. docs/ui-refactor/01-current-ui-inventory.md 与 02-performance-ux-diagnosis.md —— 现状盘点与性能基线
7. docs/ui-refactor/tools/README.md      —— 无头验证工具链（WASM 服务、MCP/CDP、像素定位、截图、trace）
8. 根 CLAUDE.md                          —— 构建/测试/i18n/文件版本等仓库约定（必须遵守）

【本轮范围】
只实施计划中的 M0（基座）与 M1（自绘画布与工作台内核），完成并验证后停止、汇报、等待审核；不要擅自进入 M2+。

【硬约束（违反即回退）】
- 不得降级 Avalonia/.NET。Avalonia 全家族补丁升级 12.1.2 → 12.1.3（Avalonia、Avalonia.Desktop、Avalonia.Browser、Avalonia.Themes.Fluent、Avalonia.Fonts.Inter 等需版本一致）。
- 引入 ReactiveUI.Avalonia 12.1.5（其依赖 ReactiveUI 25.1.1）。视图层保持普通 `UserControl`（trim 安全），VM 层用 `ReactiveObject`/`[Reactive]`/`ReactiveCommand`；如需生命周期，使用 `OnAttachedToVisualTree/OnDetachedFromVisualTree` + `CompositeDisposable`，不要用 `ReactiveUserControl`（避免 IL2026）。
- 全量编译绑定：所有 View/DataTemplate 声明 `x:DataType`，禁止 `ReflectionBinding`（现状 3 处需清除）。
- 清除全部静态可变状态（ViewModelBase.Dialog、KeyboardShortcutHandler.ShortcutConfig、FileDropHandler._host、OnboardingService 静态缓存），改 DI 单例。
- 用户数据与 7 种文件格式向后兼容；不改业务规则与文件语义；必要时才加迁移器。
- 每完成一小步都要保持可构建、可运行；中文注释与文档。
- 不提交 git（除非我明确要求）。

【验证要求（每步附证据）】
- `dotnet build`（双 TFM）与 `dotnet test` 全绿，贴关键输出。
- WASM 实机验证：`dotnet publish src/SeatFlow.Browser -c Release -p:WasmBuildNative=true -o /tmp/seatflow-web`，本地静态服务 + chrome-devtools MCP 查看（流程见 tools/README.md）。
- 环境经常重置：若 `dotnet workload list` 无 wasm-tools，先 `dotnet workload install wasm-tools`；浏览器会话可能被重置，用 tools 脚本重建确定性环境（`seed-demo-data.cs/.mjs`）。
- M1 结束时跑 Spike #2（300/800 座绘制与命中、拖拽/缩放无 >100ms 长任务、三布局切换）并留截图与 trace。

【汇报格式】
1) 完成了哪些文件/模块（路径清单）；2) 验证证据（build/test 输出、截图路径、trace 结论）；3) 与计划的偏差与原因；4) 未决问题/需要我决策的事项；5) 下一步建议（是否进入 M2）。
```
