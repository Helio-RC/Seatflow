# 02 · 性能与体验诊断（Performance & UX Diagnosis）

> 阶段 2 交付物 · 2026-10-01 · 状态：待审核（审核门 2）
> 口径：**当前工作树 HEAD 的 Release 发布**（`dotnet publish src/SeatFlow.Browser -c Release`，含原生链接），
> 内网 HeadlessChrome 153 + chrome-devtools MCP/CDP，视口 1200×800，无 CPU/网络节流。
> 数据：演示数据（64 座会场 + 240 人名单），确定性会话（无弹窗/引导）。
> 原始 trace：`assets/before/traces/*.json.gz`；分析器：`tools/analyze-trace.mjs`。

## 1. 重要口径与局限（先读）

1. **软件 WebGL**：Headless 环境回退到 SwiftShader（控制台警告 "Automatic fallback to software WebGL"）。
   因此 **Paint/Commit/GPUTask 相关数字偏悲观**；主线程 JS/布局逻辑的成本结构与桌面一致。
2. **浏览器跳过换页动画**：`MainShellViewModel.cs:118-126` 在浏览器模式跳过 200ms 淡出 + 100ms 间隔。
   故 WASM 实测的换页延迟**不含**桌面端那 ~300ms 动画延迟（`MainShellViewModel.cs:77-79,100-147`）。
3. **桌面端无法直接测量**：本文凡涉及桌面差异处均标注「推断」，置信度见 §5。
4. 单环境、单次采样；数字用于**前后对比与根因排序**，不作为绝对性能承诺。
5. 场景 A 的 LCP（80/142ms）是 canvas 元素绘制时间，**不代表应用可交互时间**（后者见 A2）。

## 2. 基线数据总表

| 场景 | trace | 长任务(>50ms) | 长任务总时长 | 最长单任务 | INP | 关键观察 |
|---|---|---|---|---|---|---|
| A 启动（auto-stop 到 load） | `A-startup` | — | — | — | — | LCP 80ms（canvas 绘制） |
| A2 完整启动（~20s 窗口） | `A2-startup-full` | **14 个** | **6075.9ms** | **1603.7ms** | — | 引导期连续 1.6s/1.5s/1.0s 阻塞；`onResize` 自身 1385.8ms |
| B 9 页连续切换（点击间隔 1.2s） | `B-pageswitch` | **36 个** | **7254.2ms** | **498.0ms** | **493ms** | `EventDispatch` 2195ms、`Commit` 1376ms；平均每次切换 ≈0.8s 主线程受阻 |
| C 会场参数连续调整（15 次 +1） | `C-venue-preview` | **47 个** | **12547.9ms** | **684.5ms** | **315ms** | `TimerFire` 7978ms；**每次参数变更 ≈0.6–0.8s 阻塞** |
| D 座位画布缩放/拖拽（5 缩进+拖拽+3 缩出） | `D-seating-interact` | **16 个** | **2788.3ms** | **264.2ms** | 22ms | 拖拽期仍有 200–260ms 级阻塞；缩放路径本身轻 |
| E 名单滚动（240 行，10 次滚轮） | `E-member-scroll` | **1 个** | 149.2ms | 149.2ms | — | 仅进入页面的加载任务；**240 行滚动未复现卡顿** |

> 长任务数量中包含启动/导航自身的初始化任务；C 的 47 个与 15 次点击强相关（每次约 3 个长任务）。

## 3. 根因分析（按影响排序）

### RC-1 会场参数变更 → 预览全量重建且无防抖（I-04）——**最高优先级**

- **证据（运行时）**：C 场景 15 次 `+1` 点击产生 **47 个长任务 / 12.5s 阻塞**，单次最长 **684.5ms**，INP **315ms**；
  `TimerFire` 累计 7978ms（每次变更触发的重建链路）。
- **证据（代码）**：约 40 个 `OnXxxChanged` 直接调用预览重建（`VenueConfigurationViewModel.cs:1051-1089`），
  重建逻辑 `RegeneratePreview`（同文件 427-673）整体重建两层 `ItemsControl+Canvas`（`VenueConfigurationView.axaml:1140-1210`）。
- **体验后果**：按住参数微调步进时界面「一顿一顿」，预览跟随严重滞后。
- **修复方向（阶段 3/5 细化）**：去抖/合帧（`DispatcherPriority` 合并或响应式 `Throttle`）+ 增量更新座位而非整表重建；
  画布渲染技术纳入选型评估（自绘 Control）。

### RC-2 页面切换的主线程编排与页面初始化（I-02）

- **证据（运行时）**：B 场景 **INP 493ms**、单次最长 498ms；`EventDispatch` 累计 2195ms、`Commit` 1376ms；
  9 次切换过程中几乎每次都有 450–500ms 级别阻塞。
- **证据（代码）**：`NavigationService.cs:46-73` 导航时同步 `GetRequiredService` 解析页面；
  8 个页面 VM 在构造器中 fire-and-forget 加载数据（Member 219 / Venue 256 / Strategy 137 / Snapshot 127 /
  Seating 177 / Settings 174 / Freeform 79 / ConfigBlock 63）；`SnapshotHistoryViewModel` 为 Transient 每次重建（`Browser/Program.cs:65`）。
- **桌面增量（推断，中置信）**：桌面还会额外支付 `MainShellViewModel.cs:100-147` 的 200ms 淡出 + 100ms 间隔，
  即桌面单次切换体感 ≈ 实测主线程阻塞 + ~300ms。
- **修复方向**：切换即时化（去掉强制动画或压缩到 <120ms 且不阻塞）；页面数据加载改为显式生命周期（进入时加载/离开时取消）；
  预热/缓存策略（Singleton 页面保留数据，但加载可取消、可复用）。

### RC-3 座位画布全量重建与模板密度（I-03）

- **证据（运行时）**：D 场景拖拽期仍有 16 个长任务 / 2.8s，最长 264ms；缩放本身 INP 22ms（轻）。
- **证据（代码）**：`BuildSeatDisplayItems` 每次整表重建 `ObservableCollection`（`SeatingArrangementViewModel.cs:413-548`）；
  每个座位一个控件，两层 `ItemsControl+Canvas`（`SeatingArrangementView.axaml:375-445`）；
  模板使用 `ReflectionBinding`（383-395、423-425、430-441），放弃编译绑定优化；
  入场初始化（本项目第一个 64 座场景）也会触发全量模板实例化。
- **修复方向**：控件元素数量与布局成本评估（自绘 Control / `ItemsRepeater` + 增量集合）；**必须**改为编译绑定；
  拖拽高频路径避免每帧重建。

### RC-4 启动期阻塞（I-09 的一部分）

- **证据（运行时）**：A2 场景 **14 个长任务 / 6075.9ms 总阻塞**，最长 1603.7 / 1458.4 / 1045.4ms；
  `onResize` 自身耗时 **1385.8ms**（`avalonia.js` ResizeObserver → canvas 尺寸调整）；`RunTask` 累计 8680ms。
- **证据（代码）**：启动链上同步/半同步工作：环境检测、AppSettings 读写、遥测初始化、
  `ScheduleAutoUpdateCheck` 等（`App.axaml.cs:435-457`）；8 个 VM 构造器加载（同上）。
- **修复方向**：启动任务分级（首帧必需 vs 后台）；避免启动即解析全部页面 VM；对 canvas resize 做防抖/尺寸稳定化。

### RC-5 其他已验证但级别较低的因素

- **E 场景（240 行名单）未复现卡顿**：滚动仅 1 个 149ms 长任务（页面加载）。名单的 `ScrollViewer>ListBox`
  虚拟化失效（I-05）在 240 行时未构成瓶颈，但 500+ 行与更多可编辑控件时仍有风险——**保留为设计约束而非当前头号问题**（修正阶段 1 的数值预期）。
- **EventDispatch/Commit 偏高**：软件 WebGL 放大，但也提示元素密度与过度重绘（每页大量 `Opacity` 过渡与多层 Borders）。桌面 GPU 下该部分预计显著缓解（推断，中置信）。
- **静态状态与订阅泄漏**（I-09）：本轮未做堆快照对比（时间预算），列为阶段 6 测试策略必测项；代码层证据见 01 §4.5。

## 4. 与阶段 1 问题清单的对应

| 阶段 1 | 诊断结论 | 状态 |
|---|---|---|
| I-02 换页 300ms 动画 | RC-2（浏览器实测未含动画，桌面叠加） | 证据强化 |
| I-03 座位画布 | RC-3（拖拽期 200-260ms 阻塞） | 证据强化 |
| I-04 会场预览无防抖 | RC-1（单次 0.6-0.8s 阻塞） | **实锤，最高优先级** |
| I-05 名单虚拟化失效 | RC-5（240 行未复现；500+ 行风险保留） | 修正范围 |
| I-09 静态状态/泄漏/构造器加载 | RC-2/RC-4（构造器加载已量化） | 部分证据 |
| I-01 小视口侧栏溢出 | 布局缺陷，与性能无关 | 保留 |

## 5. 桌面端推断与置信度

| 推断 | 依据 | 置信度 |
|---|---|---|
| 桌面换页比 WASM 更慢约 300ms | 浏览器跳过过渡、桌面执行（代码路径确定） | 高 |
| 桌面 GC/渲染开销低于软件 WebGL | 平台常识 + Commit/Paint 数据 | 中 |
| 桌面参数预览同样每变更阻塞数百 ms | 阻塞发生在 C# 主线程逻辑（与渲染后端无关） | 高 |
| 桌面 240 行滚动优于 WASM | 同上 | 中 |

## 6. 性能验收硬指标（建议稿，供阶段 6 采用）

> 测量口径必须与本文一致（同构建、同视口、同数据、同 trace 方法）。括号内为当前基线。

| # | 指标 | 目标 | 基线 |
|---|---|---|---|
| M1 | 会场参数单次变更主线程阻塞 | ≤ 50ms（INP ≤ 100ms） | ~600-800ms / INP 315ms |
| M2 | 页面切换 INP（浏览器） | ≤ 100ms | 493ms |
| M2b | 桌面换页端到端（含动画） | ≤ 250ms | 推断 ~800ms+ |
| M3 | 64 座画布首帧渲染 | ≤ 300ms | 未单独计时（含在全量重建内） |
| M3b | 画布拖拽/缩放 INP | ≤ 100ms 且无 >100ms 长任务 | 最长 264ms |
| M4 | 启动后主线程停止 >200ms 长任务的时刻 | ≤ 3s | ~6.3s（最后一个启动长任务结束） |
| M5 | 240 行名单滚动每次操作长任务 | ≤ 1 个且 <100ms | 1 个 149ms（页面加载） |
| M6 | 10 次页面往返堆增长 | ≤ 10%（待补测） | 未测 |

## 7. 复现步骤

```bash
# 1) 发布并服务（工作树根目录）
dotnet publish src/SeatFlow.Browser -c Release -o /tmp/seatflow-web
cd /tmp/seatflow-web/wwwroot && setsid nohup python3 -m http.server 8090 --bind 0.0.0.0 </dev/null >/tmp/sf-web.log 2>&1 &

# 2) 生成并注入演示数据（MCP 需先打开应用页面）
dotnet run docs/ui-refactor/tools/seed-demo-data.cs
node docs/ui-refactor/tools/seed-demo-data.mjs      # 注入 IndexedDB 并重载

# 3) 采集（MCP start_trace → seq.mjs 交互 → MCP stop_trace）
node docs/ui-refactor/tools/seq.mjs '[{"click":[70,221]},{"wait":2000},...]'
node docs/ui-refactor/tools/analyze-trace.mjs <trace.json.gz>
```

## 8. 附录：本轮未覆盖/待补

- 堆快照对比与订阅泄漏量化（M6）。
- 500+ 行名单与更多列场景。
- 真实 GPU 环境（桌面或带 GPU 的浏览器）复核 Paint/Commit 占比。
- 座位**首次生成**的端到端计时（本轮生成发生在 trace 之外；下一轮可补）。
