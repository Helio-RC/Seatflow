# ADR-007: 多策略插件包架构

- **状态**: Accepted
- **日期**: 2026-06-13
- **决策者**: Helio-RC
- **影响范围**: 插件系统、SDK、配置存储

---

## 背景

当前插件系统将"插件包"与"策略"视为 1:1 关系 — 一个 `plugin.manifest.json` 描述一个包、一个策略、以及加载指令。随着插件生态发展，需要支持一个包包含多个策略子组件（如同一包内提供"身高排序"和"前排优先"两个策略）。

## 决策

### 1. 双层清单架构

从单文件 `plugin.manifest.json` 改为**包级 `plugins-manifest.json` + 策略级 `manifest.json`** 双层清单：

```
Plugins/{packageId}/
├── plugins-manifest.json        ← 包级清单（元数据 + strategies[] 加载指令）
├── priority_fill/               ← 策略子目录
│   ├── manifest.json            ← 策略元数据（StrategyManifest 格式）
│   └── PriorityFill.dll
├── height_sort/
│   ├── manifest.json
│   └── strategy.lua
└── data/
    └── enables.json             ← 运行时启用状态
```

### 2. 职责分离

| 文件 | 内容 | 格式 |
|------|------|------|
| `plugins-manifest.json` | 包名、作者、版本、`strategies[]` 加载指令（assembly/type/scriptFile/scriptType） | `PluginPackageManifest` |
| `strategies[n]/manifest.json` | 策略 ID、显示名、优先级、参数声明、可见性 | `StrategyManifest`（与内置策略格式一致） |
| `data/enables.json` | 包级和策略级启用标志 | `PluginEnables` |

### 3. 向后兼容

- 旧 `plugin.manifest.json` 格式自动通过 `PluginManifest.ToPackageEntry()` 转换为虚拟包（1 包 = 1 策略）
- `PluginManager.LoadPluginsAsync()` 双路径扫描：检测 `plugins-manifest.json`（新）或 `plugin.manifest.json`（旧）
- 旧格式启用状态仍操作 `plugin.manifest.json` 文件，新格式使用 `data/enables.json`
- `.apairplugin` 和 `.ap-plugin` 两种扩展名均受支持

### 4. 配置存储路由

| 配置类型 | 内置策略 | 插件策略（新格式） |
|----------|---------|------------------|
| 运行时配置 | `AppData/StrategyConfig/{strategyId}.config.json` | `Plugins/{pkgId}/{strategyPath}/{strategyId}.config.json` |
| 数据集配置 | `AppData/StrategyConfig/{strategyId}/...` | `Plugins/{pkgId}/{strategyPath}/{strategyId}/...` |
| 启用状态 | 在 `StrategyConfig.IsEnabled` 中 | `Plugins/{pkgId}/data/enables.json` |

`ApplicationFacade` 通过 `_pluginManager.FindStrategy(strategyId)` 判断策略来源，自动路由到正确的存储后端。

### 5. 热插拔

- `PluginManager.RefreshPackageAsync(packageId)`: 卸载 → 重新扫描 → 加载，无需重启应用
- `PluginManager.SetStrategyEnabledAsync(strategyId, enabled)`: 运行时即时生效

### 6. 防嵌套解压

`PluginPackage.Extract()` 增加 `stripSingleFolder` 参数：若包内恰好 1 个目录 + 0 个文件，剥离外层目录。避免用户在打包时误嵌套单层文件夹。

### 7. 新数据模型

| 类 | 对应文件 | 用途 |
|----|---------|------|
| `PluginPackageManifest` | `plugins-manifest.json` | 包级清单 |
| `PluginStrategyEntry` | `strategies[]` 条目 | 单个策略的加载指令 |
| `PluginEnables` | `data/enables.json` | 运行时启用状态 |
| `LoadedPackageInfo` | — | 内存中的已加载包信息 |
| `PluginPackageDisplayInfo` | — | UI 包层级展示信息 |

## 后果

### 正面

- **多策略包**: 一个包可承载多个相关策略
- **关注点分离**: 元数据（manifest.json）与加载指令（plugins-manifest.json）解耦
- **格式统一**: 插件策略 manifest.json 与内置 `StrategyManifest` 格式完全一致
- **清晰配置路由**: 内置和插件策略的配置文件物理分离
- **热插拔**: 无需重启即可重新加载插件包

### 负面

- 文件数量增加（每个策略需要一个独立 manifest.json）
- 旧代码中存在 `[Obsolete]` 的 `PluginManifest` 类型需要逐步迁移

### 迁移路径

1. 旧格式插件无需立即迁移 — 自动转换为虚拟包继续工作
2. 新插件应使用 `plugins-manifest.json` + 策略 `manifest.json` 双层格式
3. `PluginManifest` 已标记 `[Obsolete]`，鼓励新代码使用 `PluginPackageManifest`

## 参考

- [ADR-003: 分层架构与插件化扩展设计](ADR-003-layered-architecture-with-plugins.md)
- [ADR-004: 策略模式用于座位安排引擎](ADR-004-strategy-pattern-for-seating.md)
- [ARCHITECTURE.md](../ARCHITECTURE.md)
