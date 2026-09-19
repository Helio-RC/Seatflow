# SeatFlow v2.0.0 发布说明

SeatFlow 2.0.0 正式发布：新增 **Web/WASM 浏览器支持（双壳架构）** 与 **在线版（online.seatflow.work）**，并包含此前已完成的**插件系统移除**与工程结构精简。

## 新增

- **Web/WASM 浏览器支持（双壳架构）**：`SeatFlow.Presentation.Avalonia` 转为共享类库（`net10.0;net10.0-browser`），启动逻辑拆分为 `SeatFlow.Desktop`（桌面 EXE）与 `SeatFlow.Browser`（WASM 静态站）
- **在线版（免安装）**：浏览器直接访问 [online.seatflow.work](https://online.seatflow.work) 即可使用；静态资源托管于 OSS 版本化目录、经 Cloudflare Worker 代理回源，支持 Brotli 预压缩与原子版本切换
- **浏览器端存储**：`ILocalDataStore` 抽象——桌面走文件系统，Web 走 IndexedDB；CSV/XLSX/JSON 导入导出与 `.seatsets` 打包在 Web 端等价可用
- **Web 端适配**：模态对话框 overlay 化、CJK 字体嵌入（Noto Sans SC）、DevTools Console 日志、语言预加载；PDF/图片导出、自动更新等桌面专属能力在 Web 端隐藏
- 适配 .NET 10 / Avalonia 12

## 移除

- **移除插件系统（ADR-013）**：删除 `SeatFlow.Contracts`、`SeatFlow.Plugins.Sdk`、`SeatFlow.Plugin.TestFixture` 与 `src/plugin-examples/`，移除 `NLua`、`Microsoft.CodeAnalysis.CSharp.Scripting` 依赖
- 删除插件运行时、Lua/C# 脚本策略、能力系统、插件管理页与相关文档

## 迁移说明

- 1.x 升级至 2.0.0：无需手动迁移，所有数据（会场/名单/快照/策略配置）兼容
- 如果曾安装过插件（`.ap-plugin`），插件将不再被加载，安装根目录下的 `Plugins/` 目录可手动删除

## 平台资产

- Windows x64：Setup 安装程序
- Linux x64：AppImage
- macOS：Intel 与 Apple Silicon 安装包
- 在线版：<https://online.seatflow.work>（随正式版发布自动更新）
