# Changelog

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

## [1.0.0] — 2026-06-07

### Changed
- **插件系统重构**：从单策略插件改为多策略插件包架构（`plugins-manifest.json` + 策略 `manifest.json` 双层清单），支持一个包承载多个策略和热插拔。旧格式 `plugin.manifest.json` 向后兼容，自动转换为虚拟包。新增 `.ap-plugin` 打包格式（旧 `.apairplugin` 仍受支持）。

### Added
- 插件包级管理 API（`GetPluginPackagesAsync`、`InstallPluginPackageAsync`、`UninstallPluginPackageAsync`、`RefreshPluginPackageAsync`）
- `PluginPackageConfigService` — 插件策略配置存储路由（与内置策略物理分离）
- `PluginEnables` — 运行时启用状态管理（`data/enables.json`）
- ADR-007 — 多策略插件包架构决策记录
- `PluginManager` 支持单个包热重载（`RefreshPackageAsync`）和策略级启用/禁用（`SetStrategyEnabledAsync`）
- 包级配置路由：内置策略 → `AppData/StrategyConfig/`，插件策略 → `Plugins/{pkgId}/{strategyPath}/`

### Obsoleted
- `PluginManifest` 标记为 `[Obsolete]`，新代码应使用 `PluginPackageManifest` + 策略 `manifest.json`

## [1.0.0] — 2026-06-07

### Added
- 导航区页面可导航性管理（`Data/page_navigation.json`），支持禁用页面并提示原因
- 确定性构建（`<Deterministic>true` + `<PathMap>`），相同源码产生相同 DLL
- 构建时自动注入 git commit hash 到版本号（MSBuild target `GenerateGitCommit`）
- `Data/page_navigation.json` 嵌入资源，控制各页面启用/禁用
- `Nav_PluginDisabled` 资源键（zh-CN: 插件系统尚未就绪 / en-US: Plugin system not ready）

### Changed
- 关于页面版本号改为 `about.json` + git commit hash（格式 `1.0.0+af52bf7`）
- 人员管理数据集交互重构：点击侧栏即加载、保存直接写入、切换数据集检测未保存修改
- 人员管理表格新增删除行按钮、底部空行（写完一行加一行）、保存时验证空行
- 保存前检查空行是否有未完成数据，弹窗确认

### Fixed
- 新建会场时配置区数据未清空的并发问题（取消旧 CTS + 检查取消令牌）
- 人员管理新增行布局错误（DockPanel 子元素顺序）
- 策略配置数据块 UI 硬编码中文字符串修正为 i18n（`ConfigBlock_Dataset`、`ConfigBlock_Venue`）
- 快照回滚失败异常消息去硬编码中文
- 页面禁用按钮 ToolTip 不显示（`IsEnabled=False` 改用 `Opacity`）
- `page_navigation.json` 资源加载（`AssetLoader` → `Assembly.GetManifestResourceStream`）
