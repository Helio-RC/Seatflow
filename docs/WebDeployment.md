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

- **跨源隔离**：默认单线程变体（`WasmEnableThreads=false`）不需要 COOP/COEP；
  若改用多线程变体，则必须携带以下响应头（并启用 `WasmEnableThreads=true`）：
  ```
  Cross-Origin-Opener-Policy: same-origin
  Cross-Origin-Embedder-Policy: require-corp
  ```
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
- **Skia 链接根因（已修复）**：dotnet/runtime #109289 —— SkiaSharp 原生 `.a` 在 WASM 链接时
  顺序错误导致 `sk_*` C 导出符号缺失 → `TypeInitialization_Type, SkiaSharp.SKImageInfo`。
  修复：Browser csproj 移植官方 Avalonia.Browser.targets 的
  `Issue109289_Workaround`（`_BrowserWasmWriteRspForLinking` 后重排 SkiaSharp
  链接项）。修复后单线程（`WasmEnableThreads=false`）变体即含全部 222 个 Skia 符号，
  **不再需要 COOP/COEP 跨源隔离**。已验证：splash 正常替换、canvas 接管渲染
  （无头软件 WebGL 模式），无任何 ManagedError。
- 无独立窗口：模态对话框为窗口内 overlay（行为与桌面一致，均为模态）
- 剪贴板受浏览器用户手势限制（复制类操作在点击事件内触发）
- 性能：WASM 单线程，策略执行约慢 2-4 倍（200 人级名单目标 <10s）

**白屏无输出根因链（2026-09-12 实测修复）**：

- **根因 1：生命周期接口判断错误（白屏无任何 Console 输出）**。
  浏览器 `BrowserSingleViewLifetime` 实现的是 `ISingleViewApplicationLifetime`
  （`MainView` 属性），而 `App.OnFrameworkInitializationCompleted` 误用 Android 的
  `IActivityApplicationLifetime`（`MainViewFactory`）——该分支在 WASM 下永不匹配，
  初始化块整体被跳过，且不抛异常（表现为 canvas 已创建、纯白、Console 无输出）。
  修复：改用 `ISingleViewApplicationLifetime` 并设置 `MainView`。
  **浏览器端不能构造 `Window`**（`Browser doesn't support windowing platform`），
  因此外壳拆分：`MainView : UserControl`（共享，`Views/MainView.axaml`）+
  `MainWindow : Window`（桌面外壳，仅托管 `MainView`）。
- **根因 2：裁剪默认禁用反射式 JSON** → `JsonSerializerIsReflectionDisabled`。
  修复：Browser csproj 显式 `<JsonSerializerIsReflectionEnabledByDefault>true</...>`
  （SeatFlow.* 已全量 `TrimmerRootAssembly`，反射序列化安全）。
- **根因 3：WASM 禁止同步阻塞等待** → `Cannot wait on monitors on this runtime`。
  `App.Initialize()` 内 `Task.Run(...).GetAwaiter().GetResult()` 必然失败（语言设置丢失）。
  修复：浏览器端跳过该同步路径，在 `SeatFlow.Browser/Program.Main` 于 Avalonia 启动前
  `await` 预加载语言（`App.ApplyLanguage`）。
- **根因 4：`File.Exists` 对相对路径在 WASM 恒为 false** → 每次加载都判定"首次启动"
  （引导反复触发、设置被重写）。修复：`IAppSettingsRepository` 增加存储抽象版
  `ExistsAsync()`，`App` 首次启动检测/默认设置写入改走它。
- **根因 5：无 Console 日志**：`AddSeatFlowApplication(store)` 走 `AddLogging()` 但无
  任何 Provider。修复：浏览器注册 `BrowserConsoleLoggerProvider`（转发到 DevTools Console）。
- **CJK 字体**：WASM 无系统字体，Inter 无 CJK 字形 → 中文显示为方块。
  修复：字体库嵌入 `Assets/Fonts/NotoSansSC-Regular.otf`（约 8MB，仅 `net10.0-browser`
  TFM 打包；SIL OFL 1.1，`Assets/Fonts/LICENSE.txt`），Browser 启动时注册
  `FontManagerOptions.FontFallbacks`，配合 `fonts:Inter#Inter` 主字体。桌面端不使用该字体。
