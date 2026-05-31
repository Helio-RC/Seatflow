# ADR-001: 选择 Avalonia UI 作为跨平台桌面框架

## 状态
已接受

## 日期
2025-12（项目启动时）

## 背景
项目需要一个跨平台桌面 UI 框架，目标操作系统为 Windows、macOS、Linux。核心需求：

- 原生 MVVM 支持，与 .NET 生态深度集成
- 跨平台渲染一致，不依赖各平台原生控件
- 高性能渲染（座位图可能包含数百个可交互元素）
- 活跃的社区维护与长期支持
- 允许使用自定义绘图和控件模板

## 决策
使用 **Avalonia UI 12** 作为桌面表示层框架。

## 考虑的替代方案

### WPF (.NET Framework / .NET 8+)
- 优点：最成熟的 .NET 桌面框架，文档丰富，社区庞大
- 缺点：仅支持 Windows；渲染依赖 DirectX，无法跨平台
- 拒绝：项目明确要求跨平台

### .NET MAUI
- 优点：微软官方跨平台方案，与 .NET SDK 集成
- 缺点：桌面端成熟度不足（MAUI 重点在移动端）；Linux 支持依赖社区；控件生态不完善
- 拒绝：桌面优先的场景下，MAUI 的移动优先设计不匹配

### Uno Platform
- 优点：兼容 WinUI 控件模型，支持 WebAssembly
- 缺点：抽象层过多，调试困难；自定义绘图的性能不确定
- 拒绝：项目不需要 Web/Wasm 目标，额外的抽象层增加复杂度

### Electron
- 优点：Web 技术栈，跨平台一致性好
- 缺点：内存占用高，启动慢；.NET 后端需要额外 IPC 通信层
- 拒绝：桌面座位图需要高性能渲染，Web 方案不匹配

## 后果
- 使用 Avalonia 12 的 FluentTheme 提供与 Windows 11 一致的视觉风格
- 使用 `Canvas` + 自定义控件实现座位图渲染（`CanvasZoomPan`、`ZoomOnScroll` 行为）
- Avalonia 的编译绑定（`x:CompileBindings` + `x:DataType`）提供编译时类型检查
- 依赖 SkiaSharp 作为渲染后端（通过 `Svg.Controls.Skia.Avalonia` 支持 SVG）
- 社区规模小于 WPF，遇到问题可能需要深入研究源码或提交 issue
- 跨平台测试需要在三个目标平台分别验证
