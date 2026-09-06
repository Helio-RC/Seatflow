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

- **黑屏排查**：若 WASM 已加载（无 JS 异常）但界面全黑，命令
  `chrome://gpu` 检查 WebGL2 状态。Avalonia WASM 渲染 = Skia/CanvasKit(WebGL)，
  无软件兜底；虚拟机/远程桌面/关闭硬件加速时会黑屏。开启硬件加速，
  或 `chrome://flags` 启用 `unsafe-swiftshader`（Chrome 128+ 软件 WebGL）后重试。
  对照基线：官方 `avalonia.xplat` 模板在无 WebGL 环境同样报
  `TypeInitialization_Type, SkiaSharp.SKImageInfo`（渲染栈无法初始化）。
- 无独立窗口：模态对话框为窗口内 overlay（行为与桌面一致，均为模态）
- 剪贴板受浏览器用户手势限制（复制类操作在点击事件内触发）
- 系统字体不可用：字体走嵌入 Inter 集合（`fonts:Inter#Inter`）+ 系统回退链
- 性能：WASM 单线程，策略执行约慢 2-4 倍（200 人级名单目标 <10s）
