# Spike 记录（阶段 5）

本目录存放技术选型的可复跑验证工程与证据。结论已汇总至 `../05-tech-selection.md`。

## reactiveui-probe/ — 编译级 Spike（双 TFM）

- 验证：`ReactiveUI.Avalonia 12.1.5` + `ReactiveUI 25.1.1` 在 `net10.0` 与 `net10.0-browser` 下编译通过（`[Reactive]` 源生成器、`ReactiveCommand`、`ReactiveUserControl<T>`）。
- 复跑：`cd reactiveui-probe && dotnet build`。
- 注意：包依赖 `Avalonia >= 12.1.3`（项目需从 12.1.2 补丁升级）。

## reactiveui-wasm/ — 运行时 Spike（可发布 WASM 应用）

- 同一代码库两条路径：
  - `dotnet publish -c Release -p:WasmBuildNative=true -o /tmp/spike-rui`（ReactiveUI）
  - `dotnet publish -c Release -p:UseReactiveUi=false -p:WasmBuildNative=true -o /tmp/spike-base`（纯 INPC 对照）
- 前置：`dotnet workload install wasm-tools`；csproj 内含 SkiaSharp 链接顺序 workaround。
- 验证点：页面渲染、按钮 → `ReactiveCommand` → `[Reactive]` 属性 → 绑定更新；`WhenAnyValue().Throttle(300ms)` 节流输出。

### 结果（2026-10-01，HeadlessChrome，Release + 原生链接）

| 指标 | RUI | 基线 | 差异 |
|---|---|---|---|
| `_framework` 体积 | 29.68 MB | 28.44 MB | +1.24 MB (+4.4%) |
| 启动长任务（~18s 窗口） | 9 个 / 2345 ms | 9 个 / 2129 ms | +216 ms (+10%) |
| 最长单任务 | 1087 ms | 913 ms | +174 ms |
| 裁剪警告 | 2 × IL2026（`ReactiveUserControl<T>`） | 0 | 视图用普通 `UserControl` 可规避 |
| 运行时 | ✅ `increment -> 1`、`throttled -> …` | ✅ `increment -> 1` | 均正常 |

- 证据：`../assets/spikes/startup-{rui,base}.json.gz`、`spike-rui-{before,after}.png`、`spike-base-after.png`。
- 结论：**采纳 B 路线**（见 05 文档 §2.5）。
