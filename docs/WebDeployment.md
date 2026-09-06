# SeatFlow Web 部署（WASM）

SeatFlow 浏览器版基于 Avalonia WebAssembly（`net10.0-browser`），产物为纯静态站点
（`wwwroot/`），可部署到任意静态托管（Nginx、CDN、GitHub Pages、OSS、S3……）。

## 构建与发布

```bash
# 前置：一次性的 .NET workload（每台构建机）
dotnet workload install wasm-tools

# 发布（产物：src/SeatFlow.Browser/bin/Release/net10.0-browser/publish/wwwroot）
dotnet publish src/SeatFlow.Browser -c Release

# 或使用统一脚本（打出 SeatsFlow-xxx-web.tar.gz）
cd scripts/build
./publish.sh web Release
```

## 部署要求

- **⚠️ 跨源隔离（硬性要求）**：SkiaSharp 的原生 `libSkiaSharp.a` 仅链接进
  **多线程变体** `dotnet.native.*.wasm`；多线程 WASM 要求页面处于跨源隔离状态。
  **所有静态站点的响应必须携带**：
  ```
  Cross-Origin-Opener-Policy: same-origin
  Cross-Origin-Embedder-Policy: require-corp
  ```
  Live Server (`liveServer.settings.headers`)、Nginx、IIS、CDN 配置见各自文档。
  缺失时运行时选择无 Skia 变体 → `TypeInitialization_Type, SkiaSharp.SKImageInfo` 黑屏。
- **MIME 类型**：`.wasm` → `application/wasm`（大部分现代服务器默认正确）
- **CSP**：若启用 Content-Security-Policy，需允许 `wasm-eval`（或 `unsafe-eval`）
- **压缩**：发布产物自带 `.br/.gz` 副本（参考 `wwwroot/*.br|*.gz`），CDN/服务器启用
  Brotli 压缩可使初始下载减 60-70%（未优化基线 ≈ 91MB 原始 / ~30MB 压缩）
- **路径**：默认相对路径部署；若部署在子路径，保持 `wwwroot` 目录结构整体上传

## 平台差异（Web vs 桌面）

| 能力 | 桌面 | Web |
|---|---|---|
| 数据存储 | AppData 文件系统 | IndexedDB（`seatflow`/`files`） |
| 数据迁移 | `.seatsets` 文件 | `.seatsets` 打包下载/上传（等价） |
| XLSX 导入/导出 | EPPlus | ✅ EPPlus（WASM 验证通过） |
| PDF 导出 | QuestPDF | ❌（WASM 下 QuestPDF 不可用） |
| 图片导出 | SkiaSharp | ❌（WASM 下 SkiaSharp 不可用） |
| 自动更新 / Watchdog / 单实例 | Velopack 等 | 不适用（已移除） |
| 数据目录设置 | 路径选择 | 隐藏（无文件系统） |
| 对话框 | 模态 Window | 窗口内 overlay |

Web 版在设置页隐藏"更新卡片"与"存储卡片"（路径概念不适用）；
导出菜单隐藏 PDF/图片格式。

## JS 互操作桥

模块（`wwwroot/js/`）由浏览器壳启动时 `JSHost.ImportAsync` 加载：

- `interop.js`（`sf.idb`）— IndexedDB：相对路径即 key（`Venues/…`、`Rosters/…`、
  `Assignments/…`、`StrategyConfig/…`、`AppSettings.json`），内容以 base64 桥接
- `files.js`（`sf.files`）— 文件选择（`<input type=file>`）与 Blob 下载

## 运行验证

```bash
dotnet serve -d src/SeatFlow.Browser/bin/Release/net10.0-browser/publish/wwwroot
# 打开 http://localhost:PORT
```

注意：Avalonia WASM 渲染依赖 **WebGL（CanvasKit）**。Headless/CI 无 GPU 环境
（SwiftShader 未启用）会报 `HTMLCanvasElement.getContext returned null`——
这是环境限制，真实浏览器（Chrome/Edge/Firefox）可直接使用。

## 已知限制

- **黑屏排查清单（按优先级）**：
  1. **COOP/COEP 缺失** → `TypeInitialization_Type, SkiaSharp.SKImageInfo`
     （多线程变体未选中，见"部署要求"）。Console 校验：`crossOriginIsolated` 应为 `true`。
  2. **WebGL 不可用** → `Failed to create render target` / `HTMLCanvasElement.getContext
     returned null`。`chrome://gpu` 检查 WebGL2；虚拟机/远程桌面/关闭硬件加速时黑屏。
     开启硬件加速，或 `chrome://flags` → `unsafe-swiftshader` (Chrome 128+)。
  3. **虚拟化 GPU（远程桌面/云桌面）**：`chrome://gpu` 显示 WebGL "Hardware accelerated"
     但仍黑屏，且 Console 有 `WebGL: INVALID_ENUM: getParameter: WEBGL_debug_renderer_info
     not enabled`，同时全部浏览器/全部 SkiaSharp 版本均 `TypeInitialization_Type，
     SkiaSharp.SKImageInfo` —— 这是虚拟 GPU 向 Skia 提供伪支持、GL 接口装配失败的典型
     组合。**此类环境 Avalonia WASM 无法运行**（渲染=Skia/CanvasKit(WebGL)，无软件兜底）；
     需物理机直连或 GPU 直通，或使用桌面版。本结论经纯 Skia 探针（不经 Avalonia）
     对照验证：虚拟 GPU 环境下 SkiaSharp 3.119.4 与 4.151.1 均初始化失败。
  4. 对照基线：官方 `avalonia.xplat` 模板无 COOP/COEP 或无 WebGL 时同样失败。

**技术备忘（排障证据链，2026-09-06 实测）**：
- **最终根因（已修复）**：dotnet/runtime #109289 —— SkiaSharp 原生 `.a` 在 WASM 链接时
  顺序错误导致 `sk_*` C 导出符号缺失 → `TypeInitialization_Type, SkiaSharp.SKImageInfo`。
  修复：Browser csproj 移植官方 Avalonia.Browser.targets 的
  `Issue109289_Workaround`（`_BrowserWasmWriteRspForLinking` 后重排 SkiaSharp
  链接项）。修复后单线程（`WasmEnableThreads=false`）变体即含全部 222 个 Skia 符号，
  **不再需要 COOP/COEP 跨源隔离**（部署要求大幅简化）。已验证：splash 正常替换、
  canvas 接管渲染（无头软件 WebGL 模式），无任何 ManagedError。
- SkiaSharp `libSkiaSharp.a` 仅链接进**多线程 variant**（`dotnet.native.*` 含 222 个
  `sk_`/Skia 符号，st variant 为 0）→ `WasmEnableThreads` 必须为 `true`，且部署必须
  配 COOP/COEP（`crossOriginIsolated===true`）。
- 完整条件链：COOP/COEP ✓ + WebGL ✓ + `WasmEnableThreads=true` +
  `WasmPthreadPoolInitialSize>=8`（渲染 worker 池）后，仍失败应检查第 3 条。
- 无独立窗口：模态对话框为窗口内 overlay（行为与桌面一致，均为模态）
- 剪贴板受浏览器用户手势限制（复制类操作在点击事件内触发）
- 系统字体不可用：字体走嵌入 Inter 集合（`fonts:Inter#Inter`）+ 系统回退链
- 性能：WASM 单线程，策略执行约慢 2-4 倍（200 人级名单目标 <10s）
