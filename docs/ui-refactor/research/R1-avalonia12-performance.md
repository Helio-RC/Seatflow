# R1 · Avalonia 12 性能与渲染最佳实践（官方文档摘要 + 对 SeatFlow 的映射）

> 来源：Avalonia 官方文档《Performance optimization》
> https://docs.avaloniaui.net/docs/app-development/performance （更新于 2026-09-21，抓取于 2026-10-01）
> 用途：为阶段 2 诊断基线、阶段 3 设计与阶段 5 选型提供权威依据。

## 1. UI 虚拟化

- **虚拟化需要受限高度**：放在 `StackPanel`（无限高度）或外层 `ScrollViewer` 中的 `ListBox` 会**禁用虚拟化**。
  官方推荐用 `Grid` 的 `*` 行或 `DockPanel` 填充区约束高度。
  → SeatFlow 现状：`MemberManagementView.axaml:664`、`FreeformManagementView.axaml:247` 都是
  `ScrollViewer` 包 `ListBox`，**虚拟化确定失效**（阶段 2 需量化）。
- `VirtualizingStackPanel.BufferFactor`（默认 0）可用于减少滚动回收抖动——当前项目未使用。
- 变高列表项会导致滚动条跳动与重算；建议统一行高。
- **降低控件模板复杂度**：每行多个 `TextBox`（含 watermark/clear/ScrollViewer 的深层模板）是启动与测量成本主因；
  官方建议「显示用 TextBlock，交互时切换为 TextBox」或自定义轻量 `ControlTheme`。
  → SeatFlow 现状：MemberManagement 每行 2×TextBox + ComboBox + CheckBox（数百行时显著）。

## 2. 布局

- 避免深层嵌套（每层都增加测量/排列）；用单个 Grid 替代嵌套 StackPanel。
- 属性变更（Width/Height/Margin）会触发布局失效；同一调度操作内批量设置。
  → SeatFlow 现状：多个页面存在深嵌套（详情见 01 盘点）。

## 3. 渲染

- **`IsVisible=false` 优于 `Opacity=0`**：前者跳过测量/排列/绘制，并暂停关键帧动画。
  → SeatFlow 现状：侧栏折叠用 `Opacity` + `IsHitTestVisible` 切换（MainView.axaml:51-59, 69-71 等），
  隐藏内容仍参与布局（width 过渡）——需评估收益。
- `ClipToBounds=true` 会产生裁剪层，仅在必要时使用。
- **命中测试成本随子元素数量线性增长**；大量对象场景官方建议**自定义渲染**或 overlay 命中策略，
  并对不需要交互的元素设 `IsHitTestVisible=false`。
  → SeatFlow 现状：座位画布用 `ItemsControl`+`Canvas` 逐座位生成控件（数百个元素参与命中测试与布局）。
- 减少 `BoxShadow`（每个独立渲染 pass）、避免半透明元素重叠、父级 Opacity 优于逐子级。
- **`BitmapCache`**：变化不频繁但渲染昂贵的视觉可缓存（可配 `SnapsToDevicePixels`/`EnableClearType`）。
- `RenderOptions.BitmapInterpolationMode` 低质量缩放可省算力。
- `SkiaOptions.MaxGpuResourceSizeBytes`（默认约 28MB）对大量图片/缓存的场景可调大以免逐帧重传。
- **`CompositionOptions.UseRegionDirtyRectClipping` 在 12.1 起默认关闭**（官方为帧率考虑）；
  软件渲染平台可显式开启以减少绘制面积（WASM 若回退软件渲染时值得实测）。

## 4. 数据绑定

- **编译绑定自 Avalonia 12 起默认开启**；
  → SeatFlow 现状：`SeatingArrangementView.axaml:383-395` 等座位模板显式使用 `ReflectionBinding`，
    是默认能力的例外，属明确热点。
- 常量不要用绑定；一次性数据用 `Mode=OneTime`。

## 5. 集合

- `ObservableCollection` 适合小中型列表；**大批量更新时整体替换集合优于逐项 Add**。
  → SeatFlow 现状：画布/列表采用整体重建（方向正确），但重建频率（每次参数变更）与模板实例化才是瓶颈。
- 无法虚拟化的大型非可视场景：**分批增量加载** + `Dispatcher.UIThread.Yield(DispatcherPriority.Background)`。
  → 可借鉴到画布首次构建与大数据导入。

## 6. 异步与线程

- 重计算放后台线程（`Task.Run`）。
- **去抖**：搜索/连续输入场景用 `Throttle`（文档示例即 ReactiveUI 的 `WhenAnyValue().Throttle()`）。
  → SeatFlow 现状：VenueConfiguration 约 40 个参数回调直接触发预览全量重建，无去抖（阶段 2 重点）。
- 低优先级工作用 `DispatcherPriority.Background` 延后。

## 7. 性能剖析工具

- DevTools（F12，Debug 构建）Performance 页、dotTrace/dotMemory、FPS overlay。
- 无头环境替代：chrome-devtools MCP + CDP trace（本仓库已验证可用，见 `tools/README.md`）。

## 8. 对阶段 2 的测量建议（由本文档导出）

| 场景 | 观测指标 | 方法 |
|---|---|---|
| 会场参数连续调整 | 主线程长任务、Layout/Measure 时长、帧间隔 | CDP trace（交互期手动 stop） |
| 座位画布拖拽/缩放 | 输入→呈现延迟、每帧时长、命中测试耗时 | CDP trace + 长任务 |
| 页面切换/启动 | LCP、启动期长任务、模板实例化 | trace（已验证可产出洞察） |
| 名单滚动（数百行） | 帧率、GC、布局耗时 | 注入演示数据后 trace + heap 快照 |
| 内存/订阅泄漏 | 堆增长趋势、监听器数量 | 堆快照对比（多次页面往返） |
