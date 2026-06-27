# .seatsets 数据包格式规范

## 概述

`.seatsets` 是 SeatFlow 应用数据包文件格式，用于将 AppData 目录下的所有应用数据（应用设置、会场布局、学生数据集、座位快照、策略配置）打包为单个文件，支持备份、迁移和跨设备数据恢复。

## 文件属性

| 属性 | 值 |
|------|-----|
| 扩展名 | `.seatsets` |
| MIME 类型 | `application/x-seatflow-seatsets` |
| 最大文件大小 | 200 MB |
| 编码格式 | UTF-8 |
| 序列化格式 | JSON（缩进 + camelCase） |
| 当前格式版本 | `1.0` |

## JSON 结构

```json
{
  "formatVersion": "1.0",
  "appVersion": "1.0.0+abc1234",
  "createdAt": "2026-06-27T12:00:00+08:00",
  "description": null,
  "chunks": {
    "appSettings": {
      "hash": "sha256-hex-string",
      "files": {
        "AppSettings.json": { /* AppSettings 原始 JSON */ }
      }
    },
    "venues": {
      "hash": "sha256-hex-string",
      "files": {
        "Venues/venue-uuid.venue.json": { /* VenueFile 原始 JSON */ }
      }
    },
    "rosters": {
      "hash": "sha256-hex-string",
      "files": {
        "Rosters/roster-uuid.roster.json": { /* RosterFile 原始 JSON */ }
      }
    },
    "snapshots": {
      "hash": "sha256-hex-string",
      "files": {
        "Assignments/venueId/yyyymmdd/snapshotId.json": { /* SeatingSnapshot 原始 JSON */ },
        "Assignments/venueId/_venue.json": { /* VenueSnapshotInfo 原始 JSON */ }
      }
    },
    "strategyConfig": {
      "hash": "sha256-hex-string",
      "files": {
        "StrategyConfig/strategyId.config.json": { /* StrategyConfig 原始 JSON */ },
        "StrategyConfig/strategyId/dataset.config.json": { /* StrategyDatasetConfig 原始 JSON */ }
      }
    }
  },
  "archiveHash": "sha256-hex-string"
}
```

### 字段说明

| 字段 | 类型 | 说明 |
|------|------|------|
| `formatVersion` | string | 文件格式版本号，当前为 `"1.0"` |
| `appVersion` | string | 创建此归档时的应用版本号 |
| `createdAt` | string | ISO 8601 格式的创建时间戳 |
| `description` | string? | 用户可选的描述信息 |
| `chunks` | object | 数据块字典，key 为类别名，value 为该类别的数据块 |
| `archiveHash` | string | 整体归档的 SHA256 哈希（小写十六进制） |

## 数据类别

| 类别 Key | 说明 | 对应 AppData 目录 |
|----------|------|-------------------|
| `appSettings` | 应用设置 | `AppSettings.json` |
| `venues` | 会场布局 | `Venues/*.venue.json` |
| `rosters` | 学生数据集 | `Rosters/*.roster.json` |
| `snapshots` | 座位快照 | `Assignments/**/*.json` |
| `strategyConfig` | 策略配置 | `StrategyConfig/**/*.config.json` |

## 哈希计算规则

### Chunk 哈希

每个 chunk 的 `hash` 字段是对其 `files` 字典做**确定性 JSON 序列化**后取 SHA256：

1. 将 `files` 字典按 key（文件路径）字典序排序
2. 使用 `Utf8JsonWriter` 序列化为紧凑 JSON（无缩进、无额外空白）
3. 对 UTF-8 字节序列取 SHA256
4. 输出为小写十六进制字符串

### Archive 哈希

顶层 `archiveHash` 是对所有 chunk hash 拼接后取 SHA256：

1. 将所有 chunk 按类别 key 字典序排序
2. 拼接每个 chunk 的 `hash` 值（空字符串表示无内容）
3. 对拼接结果取 SHA256
4. 输出为小写十六进制字符串

### 校验流程

导入时的完整性校验顺序：
1. **文件大小校验**：超过 200 MB 直接拒绝
2. **JSON 解析校验**：确保是合法的 JSON 且符合格式结构
3. **Chunk 哈希校验**：逐块验证 `files` 内容与 `hash` 匹配
4. **Archive 哈希校验**：验证所有 chunk hash 与 `archiveHash` 匹配

## 路径约定

- 存档中所有文件路径使用正斜杠 `/` 作为分隔符，与操作系统无关
- 导入时自动转换为当前平台的路径分隔符
- 路径相对于 AppData 根目录（例如 `Venues/venue-001.venue.json`）

## 版本迁移

`.seatsets` 格式支持通过 `FileMigrationService` 进行版本迁移。当前注册的迁移器：

| 迁移器 | 从版本 | 到版本 | 说明 |
|--------|--------|--------|------|
| `SeatSetsMigrators.Step_1_0_to_1_1` | 1.0 | 1.1 | 占位（no-op），为未来格式升级预留 |

当加载旧版本归档时，迁移器链式执行，逐版本升级到当前格式。

## 导入行为

### 尽力而为策略

导入采用"尽力而为"策略：
- 单个 chunk 哈希不匹配 → 跳过该 chunk 所有文件，继续处理其他 chunk
- 单个文件写入失败 → 记录错误到结果中，继续处理下一个文件
- 已经在目标位置存在的文件将被覆盖（导入 = 恢复数据）

### 导入结果

```csharp
public class SeatSetsImportResult
{
    public bool Success;       // 无任何错误
    public int TotalFiles;     // 尝试导入的文件总数
    public int Restored;       // 成功恢复的文件数
    public int Skipped;        // 跳过的文件数
    public int Failed;         // 失败的文件数 (= Total - Restored - Skipped)
    public List<string> Errors; // 错误消息列表
}
```

## 导入途径

### 1. 设置页面按钮导入

用户在 Settings 页面点击"导入数据..."按钮 → 选择 `.seatsets` 文件 → 校验 → 显示类别选择对话框 → 确认导入 → 显示结果。

### 2. 双击文件打开

操作系统将 `.seatsets` 文件关联到 SeatFlow → 双击文件启动程序 → 显示导入选择对话框 → 确认导入。

文件关联需要用户手动配置（参见下方"文件关联注册"）。

### 3. 自动发现（首次启动）

程序启动时检测到 AppData 目录不存在 → 扫描可执行文件目录中的 `.seatsets` 文件 → 取最新文件 → 静默全量导入（无需用户确认）。

此功能用于新设备上的快速数据迁移：将 `.seatsets` 文件与可执行文件放在同一目录即可。

## 文件关联注册

### Windows

通过注册表将 `.seatsets` 关联到 SeatFlow：

```reg
Windows Registry Editor Version 5.00
[HKEY_CURRENT_USER\Software\Classes\.seatsets]
@="SeatFlow.seatsets"
[HKEY_CURRENT_USER\Software\Classes\SeatFlow.seatsets\shell\open\command]
@="\"C:\\path\\to\\SeatFlow.exe\" \"%1\""
```

### Linux

创建 MIME 类型和桌面条目：

```bash
# ~/.local/share/mime/packages/seatsets.xml
<?xml version="1.0" encoding="UTF-8"?>
<mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
  <mime-type type="application/x-seatflow-seatsets">
    <comment>SeatFlow Data Package</comment>
    <glob pattern="*.seatsets"/>
  </mime-type>
</mime-info>
```

```bash
update-mime-database ~/.local/share/mime
```

### macOS

在 `Info.plist` 中添加：

```xml
<key>CFBundleDocumentTypes</key>
<array>
  <dict>
    <key>CFBundleTypeName</key>
    <string>SeatFlow Data Package</string>
    <key>CFBundleTypeRole</key>
    <string>Editor</string>
    <key>LSHandlerRank</key>
    <string>Owner</string>
    <key>LSItemContentTypes</key>
    <array>
      <string>com.seatflow.seatsets</string>
    </array>
  </dict>
</array>
```
