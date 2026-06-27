# SeatFlow.Plugins.Sdk — 插件开发 SDK

SeatFlow 座位安排系统的插件开发工具包。引用此 SDK 即可开发自定义排座策略插件，无需依赖主程序集。

---

## 插件架构概述

插件系统采用 **双层清单架构**（ADR-007），一个插件包可承载多个策略子组件：

```
Plugins/{packageId}/
├── plugins-manifest.json        ← 包级清单（元数据 + strategies[] 加载指令）
├── strategy_a/                  ← 策略子目录
│   ├── manifest.json            ← 策略元数据（与内置策略 StrategyManifest 格式一致）
│   └── StrategyA.dll
├── strategy_b/
│   ├── manifest.json
│   └── strategy.lua
└── data/
    └── enables.json             ← 运行时启用状态
```

接口层次（通用到具体）：

```
IPlugin                  ← 所有插件的身份契约（Id, Name, Version, Category）
  └── IPluginSeatingStrategy  ← 排座策略插件契约（+ Priority, IsEnabled, ExecuteAsync）

IPluginLifecycle         ← 可选生命周期管理（InitializeAsync, DisposeAsync）
IPluginHost              ← 插件初始化时获取的宿主服务入口
```

`Category` 字段用于功能路由，内置类别：
| Category | 状态 | 说明 |
|----------|------|------|
| `"strategy"` | **已实现** | 排座策略插件 |
| `"provider"` | 预留（中期） | 数据导入插件 |
| `"exporter"` | 预留（中期） | 导出器插件 |

> **注意**：旧版单文件 `plugin.manifest.json` 格式（v1.0）在 v1.1.0 已移除。当前仅支持双层清单格式。`.apairplugin` 和 `.ap-plugin` 两种扩展名均可加载。

---

## 快速开始

### 1. 创建插件项目

```bash
dotnet new classlib -n MyPlugin
cd MyPlugin
dotnet add reference /path/to/SeatFlow/SeatFlow.Plugins.Sdk/SeatFlow.Plugins.Sdk.csproj
```

### 2. 实现策略

```csharp
using SeatFlow.Contracts.Interfaces;
using SeatFlow.Contracts.Models;

namespace MyPlugin;

public class MyStrategy : IPluginSeatingStrategy
{
    // ─── 以下来自 IPlugin ───
    public string Id => "my-first-plugin";
    public string Name => "我的第一个插件";
    public string Version => "1.0.0";
    public string Category => "strategy";

    // ─── 以下来自 IPluginSeatingStrategy ───
    public int Priority { get; set; } = 50;
    public bool IsEnabled { get; set; } = true;

    public Task<PluginStrategyResult> ExecuteAsync(
        IPluginWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var emptySeats = workspace.GetEmptySeats().ToList();
        var assigned = workspace.GetAssignments().Values.ToHashSet();
        var unassigned = workspace.Students
            .Where(s => !assigned.Contains(s.Id))
            .ToList();

        for (int i = 0; i < Math.Min(unassigned.Count, emptySeats.Count); i++)
            workspace.TryAssignSeat(emptySeats[i].Id, unassigned[i].Id, out _);

        return Task.FromResult(new PluginStrategyResult
        {
            Success = true,
            Message = $"已分配 {unassigned.Count} 名学生"
        });
    }
}
```

### 3. 创建清单文件

需要两个清单文件：

**包级清单** — 项目根目录下 `plugins-manifest.json`：

```json
{
  "id": "my-first-package",
  "name": "我的第一个插件包",
  "version": "1.0.0",
  "author": "Your Name",
  "description": "一个简单的排座策略示例包",
  "strategies": [
    {
      "name": "simple-fill",
      "assembly": "MyPlugin.dll",
      "type": "MyPlugin.MyStrategy",
      "priority": 50,
      "enabled": true
    }
  ]
}
```

**策略清单** — `simple-fill/manifest.json`（策略子目录下）：

```json
{
  "id": "simple-fill",
  "name": { "zh-CN": "简单填充", "en-US": "Simple Fill" },
  "description": { "zh-CN": "将未分配学生随机填入空座位", "en-US": "Fills unassigned students into empty seats" },
  "priority": 50,
  "isIndependent": true,
  "parameters": [],
  "codeBlocks": []
}
```

> **单策略起步**：包内只需一个策略子目录。旧版 `plugin.manifest.json` 格式已在 v1.1.0 移除，不可用。

### 4. 构建并部署

```bash
dotnet build -c Release
# 将输出文件复制到 SeatFlow 的插件目录：
# Plugins/my-first-package/
#   ├── plugins-manifest.json
#   ├── simple-fill/
#   │   ├── manifest.json
#   │   └── MyPlugin.dll
#   └── data/ (可选)
```

---

## 项目结构

### SDK 提供的内容

| 路径 | 类型 | 用途 |
|------|------|------|
| `Attributes/PluginAttribute.cs` | `[Plugin]` 特性 | 可选的类级别元数据注解，字段与 `PluginPackageManifest` 对应 |
| `Abstractions/PluginBase.cs` | 抽象基类 | 实现 IPlugin 身份元数据的基类，减少样板代码 |
| `Abstractions/PluginStrategyBase.cs` | 抽象基类 | 继承 PluginBase，扩展策略执行相关成员 |
| `Models/PluginPackage.cs` | 静态工具类 | `.ap-plugin` / `.apairplugin` 打包/解包/验证 |

### 通过传递引用可用的类型

SDK 引用 `SeatFlow.Contracts`。插件作者通过 `IPluginWorkspace` 接口与工作区交互（不在 SDK 中，由主程序传入）：

| 类型 | 命名空间 | 来源 |
|------|----------|------|
| `IPlugin` | `SeatFlow.Contracts.Interfaces` | Contracts |
| `IPluginLifecycle` | `SeatFlow.Contracts.Interfaces` | Contracts |
| `IPluginHost` | `SeatFlow.Contracts.Interfaces` | Contracts |
| `IPluginSeatingStrategy` | `SeatFlow.Contracts.Interfaces` | Contracts |
| `PluginStrategyResult` | `SeatFlow.Contracts.Interfaces` | Contracts |
| `IPluginConfigurationService` | `SeatFlow.Contracts.Interfaces` | Contracts |
| `IPluginWorkspace` | `SeatFlow.Contracts.Models` | Contracts |
| `IPluginStudent` | `SeatFlow.Contracts.Models` | Contracts |
| `IPluginSeat` | `SeatFlow.Contracts.Models` | Contracts |

> **注意：** 插件代码中应使用 `IPluginWorkspace`（而非 `SeatingWorkspace`），`IPluginSeat`（而非 `GridSeat`/`PolarSeat`），以及 `IPluginStudent`（而非 `Student`）。主程序在运行时会将这些接口的具体实现注入到策略的 `ExecuteAsync` 方法中。

---

## 接口参考

### IPlugin（基础身份）

所有插件的通用身份契约：

```csharp
namespace SeatFlow.Contracts.Interfaces;

public interface IPlugin
{
    string Id { get; }        // 插件唯一标识符
    string Name { get; }      // 插件显示名称
    string Version { get; }   // 插件版本号
    string Category { get; }  // 功能类别（"strategy"/"provider"/"exporter"）
}
```

### IPluginLifecycle（可选生命周期）

实现此接口的插件将由 PluginManager 自动管理生命周期：

```csharp
public interface IPluginLifecycle
{
    Task InitializeAsync(IPluginHost host, CancellationToken ct = default);
    Task DisposeAsync();
}
```

- `InitializeAsync` — 插件加载后调用，可获取宿主服务
- `DisposeAsync` — 插件卸载时调用，用于释放文件句柄、网络连接等资源

### IPluginHost

初始化时获得的宿主服务入口：

```csharp
public interface IPluginHost
{
    IPluginConfigurationService Configuration { get; }  // 配置读写服务
    string PluginDirectory { get; }                     // 插件所在目录路径
}
```

### IPluginSeatingStrategy

排座策略插件契约，**继承 IPlugin**：

```csharp
namespace SeatFlow.Contracts.Interfaces;

public interface IPluginSeatingStrategy : IPlugin
{
    string IPlugin.Category => "strategy";   // 默认类别
    string IPlugin.Version => "1.0.0";       // 默认版本

    int Priority { get; set; }               // 执行优先级（数值越大越先执行）
    bool IsEnabled { get; set; }             // 是否启用

    Task<PluginStrategyResult> ExecuteAsync(
        IPluginWorkspace workspace,
        CancellationToken cancellationToken);
}
```

### PluginStrategyResult

```csharp
public class PluginStrategyResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
```

设置 `Success = true` 表示策略执行成功。若 `Success = false`，系统会记录失败但不会中断整个管道。

---

## 基类参考

### PluginBase

实现 `IPlugin` 的抽象基类，自动从 `[Plugin]` 特性读取元数据：

```csharp
namespace SeatFlow.Plugins.Sdk.Abstractions;

public abstract class PluginBase : IPlugin
{
    public string Id { get; }        // 来自 [Plugin].Id，否则随机 GUID
    public string Name { get; }      // 来自 [Plugin].Name，否则类型名
    public string Version { get; }   // 来自 [Plugin].Version，否则 "1.0.0"
    public string Category { get; }  // 来自 [Plugin].Category，否则 "strategy"
}
```

### PluginStrategyBase

继承 `PluginBase`，扩展策略执行能力：

```csharp
public abstract class PluginStrategyBase : PluginBase, IPluginSeatingStrategy
{
    public int Priority { get; set; }     // 来自 [Plugin].Priority，默认 50
    public bool IsEnabled { get; set; }   // 来自 [Plugin].Enabled，默认 true

    public abstract Task<PluginStrategyResult> ExecuteAsync(
        IPluginWorkspace workspace, CancellationToken ct);
}
```

**继承链示意：**
```
PluginBase (I: Id, Name, Version, Category)
  └── PluginStrategyBase (+ Priority, IsEnabled, + abstract ExecuteAsync)
```

### PluginAttribute

声明式元数据注解，字段与 `PluginPackageManifest` 对应：

```csharp
[Plugin("my-strategy",                      // 必选：插件 ID
    Name = "自定义策略",                     // 可选，默认类名
    Version = "1.0.0",                      // 可选，默认 "1.0.0"
    Category = "strategy",                  // 可选，默认 "strategy"
    Description = "按身高分配座位",           // 可选
    Author = "张三",                         // 可选
    Priority = 30,                          // 可选，默认 50
    Enabled = true)]                        // 可选，默认 true
public class MyStrategy : PluginStrategyBase { ... }
```

当类继承 `PluginBase` 或 `PluginStrategyBase` 时，基类构造函数会自动读取特性值填充对应属性。

---

## 清单文件体系

v1.1+ 采用 **双层清单架构**（ADR-007）：包级 `plugins-manifest.json` 负责加载指令，策略级 `manifest.json` 负责元数据与声明式配置。旧版单文件 `plugin.manifest.json` 已在 v1.1.0 移除。

### 包级清单：plugins-manifest.json

定义包的身份和各策略子组件的加载方式。

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | **是** | 包唯一标识符 |
| `name` | string | 否 | 包显示名称 |
| `version` | string | 否 | 语义化版本号（默认 `"1.0.0"`） |
| `description` | string | 否 | 包功能描述 |
| `author` | string | 否 | 作者 |
| `strategies` | array | **是** | 策略加载指令列表，每项为 `PluginStrategyEntry` |

**PluginStrategyEntry**（`strategies[]` 条目）：

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `name` | string | **是** | 策略子目录名（也用作默认显示名） |
| `assembly` | string | 见注 | DLL 文件名（程序集策略） |
| `type` | string | 见注 | 入口类型完全限定名（程序集策略） |
| `scriptFile` | string | 见注 | 脚本文件名（脚本策略） |
| `scriptType` | string | 见注 | 脚本类型：`"lua"` 或 `"csharp"` |
| `priority` | int | 否 | 默认优先级（默认 50） |
| `enabled` | bool | 否 | 默认是否启用（默认 true） |

> **注：** 程序集策略需同时提供 `assembly` + `type`；脚本策略需同时提供 `scriptFile` + `scriptType`。两者互斥。

### 策略清单：manifest.json

每个策略子目录下的 `manifest.json` 格式与内置策略的 `StrategyManifest` 完全一致。字段：

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | **是** | 策略唯一标识符 |
| `name` | dict | 否 | i18n 显示名 `{"zh-CN":"...","en-US":"..."}` |
| `description` | dict | 否 | i18n 描述 |
| `priority` | int | 否 | 执行优先级（默认来自包级清单，可覆盖） |
| `isIndependent` | bool | 否 | `true`=独立策略（外部管道）；`false`=依赖策略（RandomFill 上下文）。默认 `true` |
| `visible` | bool | 否 | 是否参与管道（默认 `true`） |
| `manifestVersion` | string | 否 | Manifest 格式版本号（默认 `"1.0"`） |
| `capabilities` | string[] | 否 | 能力声明（如 `"MarkFixedSeat"`） |
| `parameters` | array | 否 | 策略级全局参数声明（见下方"声明式配置"） |
| `codeBlocks` | array | 否 | 按数据集/会场的配置块声明（见下方"声明式配置"） |
| `messages` | dict | 否 | 策略执行消息 i18n 模板 |

### 依赖策略（Dependent Strategy）

设置 `"isIndependent": false` 可使插件策略作为**依赖策略**执行。依赖策略不在外部管道中执行，而是在 RandomFill 的分配循环中运行：

```
RandomFill 随机选 (student, seat) →
  依次调用依赖策略 EvaluateAsync (按内部 Priority) →
    批准 (Approve) → 继续分配
    拒绝 (Reject) → 换座位重试（有上限）
    已处理 (Handled) → 策略已完成分配（含连携修改），跳过 TryAssignSeat
```

**注意：** 当前版本的 `IPluginSeatingStrategy` 接口尚未支持 `EvaluateAsync`，因此插件依赖策略将默认批准所有分配。后续版本会扩展该接口。

### 声明式配置与 i18n

插件通过 `parameters` 和 `codeBlocks` 声明配置 UI。所有用户可见文字使用内嵌多语言词典：

```json
"label": { "zh-CN": "历史惩罚权重", "en-US": "History Penalty Weight" }
```

**parameters 示例：**

```json
"parameters": [
  {
    "name": "minScore",
    "fieldType": "NumberInput",
    "label": { "zh-CN": "最低分数", "en-US": "Minimum Score" },
    "defaultValue": 50,
    "minValue": 0,
    "maxValue": 100
  }
]
```

支持的 `fieldType`：`NumberInput`、`TextInput`、`ToggleSwitch`、`Dropdown`。

**codeBlocks 示例：**

```json
"codeBlocks": [
  {
    "title": { "zh-CN": "优先分配", "en-US": "Priority Assignment" },
    "dataType": "Student",
    "displayMode": "ValuePair",
    "fields": [
      { "name": "student", "fieldType": "StudentPicker",
        "label": { "zh-CN": "学生", "en-US": "Student" } }
    ]
  }
]
```

`dataType`：`Student`、`Venue`、`Both`。
`displayMode`：`Table`（表格模式）、`ValuePair`（值对模式）。
`showSeatPosition`（可选，默认 `true`）：`false` 时隐藏座位定位器，适用于自动匹配策略。
`preventDuplicateInRow`（可选，默认 `false`）：`true` 时禁止同行内学生选择器值重复。
`preventDuplicateAcrossRows`（可选，默认 `false`）：`true` 时禁止跨行学生选择器值重复。
`loadTrigger`（可选，默认 `Both`）：控制 `dataType:Both` 时配置加载的触发方式。
codeBlock 中额外支持的 `fieldType`：`StudentPicker`、`SeatPosition`。

UI 层通过 `LocalizeHelper.Resolve(dict)` 按 `CultureInfo.CurrentUICulture` 解析词典，回退顺序：当前语言 → zh-CN → 字典第一项。

### 新版清单示例

**包级** `plugins-manifest.json`：

```json
{
  "id": "my-seating-pack",
  "name": "排座策略包",
  "version": "1.0.0",
  "description": "包含身高优先和前排优先两个策略",
  "author": "张三",
  "strategies": [
    {
      "name": "height-desc",
      "assembly": "HeightStrategy.dll",
      "type": "MyPlugin.HeightStrategy",
      "priority": 30,
      "enabled": true
    },
    {
      "name": "front-row-fill",
      "scriptFile": "strategy.lua",
      "scriptType": "lua",
      "priority": 40,
      "enabled": false
    }
  ]
}
```

**策略级** `height-desc/manifest.json`：

```json
{
  "id": "height-desc",
  "name": { "zh-CN": "身高降序分配", "en-US": "Height Descending" },
  "description": { "zh-CN": "按身高从高到低分配座位", "en-US": "Assign seats by height descending" },
  "isIndependent": true,
  "parameters": [
    {
      "name": "reverse",
      "fieldType": "ToggleSwitch",
      "label": { "zh-CN": "反向（矮个优先）", "en-US": "Reverse (short first)" },
      "defaultValue": false
    }
  ]
}
```

---

## 旧版 plugin.manifest.json 规范（v1.0 历史参考）

> v1.0 单文件格式，已在 v1.1.0 移除。仅供参考，不可用于新插件。

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|------|------|------|--------|------|
| `id` | string | **是** | — | 插件唯一标识符 |
| `name` | string | 否 | `""` | 显示名称 |
| `version` | string | 否 | `"1.0.0"` | 语义化版本号 |
| `category` | string | 否 | `"strategy"` | 功能类别 |
| `description` | string | 否 | `""` | 功能描述 |
| `author` | string | 否 | `""` | 作者 |
| `assembly` | string | 见注 | `""` | DLL 文件名（程序集插件） |
| `type` | string | 见注 | `""` | 入口类型完全限定名 |
| `priority` | int | 否 | `50` | 执行优先级 |
| `enabled` | bool | 否 | `true` | 默认启用 |
| `scriptFile` | string | 见注 | null | 脚本文件名 |
| `scriptType` | string | 见注 | null | `"lua"` 或 `"csharp"` |
| `visible` | bool | 否 | `true` | 是否参与管道 |
| `isIndependent` | bool | 否 | `true` | 独立/依赖策略 |
| `manifestVersion` | string | 否 | `"1.0"` | Manifest 格式版本 |
| `parameters` | array | 否 | null | 策略参数声明 |
| `codeBlocks` | array | 否 | null | 配置块声明 |

> **注：** 程序集插件需同时提供 `assembly` + `type`；脚本插件需同时提供 `scriptFile` + `scriptType`。两者互斥。

### 旧版单文件示例

**程序集插件** `plugin.manifest.json`：

```json
{
  "id": "height-strategy",
  "name": "身高优先排座",
  "version": "1.0.0",
  "category": "strategy",
  "description": "按学生身高降序分配座位",
  "author": "张三",
  "assembly": "HeightStrategy.dll",
  "type": "MyPlugin.HeightStrategy",
  "priority": 30,
  "enabled": true
}
```

**Lua 脚本** `plugin.manifest.json`：

```json
{
  "id": "lua-random-fill",
  "name": "Lua 随机填充",
  "version": "1.0.0",
  "category": "strategy",
  "scriptFile": "strategy.lua",
  "scriptType": "lua",
  "priority": 60,
  "enabled": true
}
```

---

## IPluginWorkspace API 参考

`ExecuteAsync` 接收一个 `IPluginWorkspace` 实例，这是插件视角的工作区契约。主程序在运行时将 `SeatingWorkspace`（实现了 `IPluginWorkspace`）注入。

### 属性

| 成员 | 类型 | 说明 |
|------|------|------|
| `Students` | `IReadOnlyList<IPluginStudent>` | 所有待分配学生（只读） |

### 方法

**`bool TryAssignSeat(string seatId, string studentId, out string error)`**

将学生分配到座位。验证逻辑：
- 座位和学生必须存在
- 座位必须可用（`IsAvailable == true`）
- 固定座位只能分配给指定学生
- 同一学生不可已分配到其他座位

**`IEnumerable<IPluginSeat> GetEmptySeats()`**

返回所有可用且非固定的空座位。

**`IEnumerable<IPluginSeat> FindSeats(Func<IPluginSeat, bool> predicate)`**

按条件查找座位。示例：
```csharp
var available = workspace.FindSeats(s => s.IsAvailable);
var fixedSeats = workspace.FindSeats(s => s.IsFixed);
```

**`IReadOnlyDictionary<string, string> GetAssignments()`**

返回当前座位分配快照（座位 ID → 学生 ID）。Key = 座位 ID，Value = 学生 ID。

**`void LogWarning(string strategyId, string displayName, string messageKey, params object?[] args)`**

记录一条警告消息，执行完成后自动汇总到座位安排页的侧栏信息面板中。`messageKey` 对应 manifest `messages` 中的 i18n 键，`args` 为 `string.Format` 参数。

**`void LogError(string strategyId, string displayName, string messageKey, params object?[] args)`**

同上，严重级别为错误。

**manifest `messages` 字段**（可选）：
```json
"messages": {
  "MyPlugin_NoSeats": {
    "zh-CN": "无法为组（{0}）找到连续座位",
    "en-US": "Could not find contiguous seats for group ({0})"
  }
}
```
策略内调用：`workspace.LogWarning(Id, "我的策略", "MyPlugin_NoSeats", groupInfo);`

---

## 数据模型参考

插件通过 `IPluginWorkspace` 接口与以下只读模型交互：

### IPluginStudent

| 属性 | 类型 | 说明 |
|------|------|------|
| `Id` | `string` | 唯一标识符（GUID） |
| `Name` | `string` | 学生姓名 |
| `Height` | `float?` | 身高（厘米），可为 null |
| `NeedsFrontRow` | `bool` | 是否需要前排座位 |
| `FrontRowPreferenceScore` | `int` | 前排偏好分数，越大越优先 |

### IPluginSeat

| 属性 | 类型 | 说明 |
|------|------|------|
| `Id` | `string` | 唯一标识符（GUID） |
| `IsAvailable` | `bool` | 是否可用 |
| `IsFixed` | `bool` | 是否固定座位 |
| `OccupantId` | `string?` | 当前占用学生 ID，null 表示空 |

> **注意：** 插件代码中不可直接使用 `Student`、`Seat`、`GridSeat`、`PolarSeat`、`AttributeBag` 等 Core 层类型。这些类型仅在主程序内部使用，插件应通过上述 Contracts 接口与系统交互。

---

## 程序集插件完整示例

### 方式一：继承 PluginStrategyBase（推荐，最少代码）

```csharp
using SeatFlow.Plugins.Sdk.Attributes;
using SeatFlow.Plugins.Sdk.Abstractions;
using SeatFlow.Contracts.Models;

[Plugin("height-desc", Name = "身高降序分配",
    Description = "按身高从高到低分配座位", Author = "张三", Priority = 25)]
public class HeightDescendingStrategy : PluginStrategyBase
{
    // Id, Name, Version, Category 从 PluginBase 继承，自动从 [Plugin] 读取
    // Priority, IsEnabled 自动从 [Plugin] 读取
    // 只需实现 ExecuteAsync！

    public override Task<PluginStrategyResult> ExecuteAsync(
        IPluginWorkspace workspace, CancellationToken ct)
    {
        var assigned = workspace.GetAssignments().Values.ToHashSet();
        var sorted = workspace.Students
            .Where(s => !assigned.Contains(s.Id))
            .OrderByDescending(s => s.Height ?? 0)
            .ToList();

        var seats = workspace.GetEmptySeats().ToList();

        for (int i = 0; i < Math.Min(sorted.Count, seats.Count); i++)
            workspace.TryAssignSeat(seats[i].Id, sorted[i].Id, out _);

        return Task.FromResult(new PluginStrategyResult
        { Success = true, Message = "完成" });
    }
}
```

### 方式二：直接实现接口

```csharp
using SeatFlow.Contracts.Interfaces;
using SeatFlow.Contracts.Models;

namespace MyPlugin;

public class FrontRowPriority : IPluginSeatingStrategy
{
    public string Id => "front-row-priority";
    public string Name => "前排优先";
    public string Version => "1.0.0";
    public string Category => "strategy";
    public int Priority { get; set; } = 20;
    public bool IsEnabled { get; set; } = true;

    public Task<PluginStrategyResult> ExecuteAsync(
        IPluginWorkspace workspace, CancellationToken ct)
    {
        var needsFront = workspace.Students
            .Where(s => s.NeedsFrontRow)
            .OrderByDescending(s => s.FrontRowPreferenceScore)
            .ToList();

        var frontSeats = workspace.FindSeats(s =>
            s.IsAvailable && s.Id.StartsWith("R1")).ToList();

        for (int i = 0; i < Math.Min(needsFront.Count, frontSeats.Count); i++)
            workspace.TryAssignSeat(frontSeats[i].Id, needsFront[i].Id, out _);

        return Task.FromResult(new PluginStrategyResult
        { Success = true, Message = $"已为 {needsFront.Count} 名学生分配前排" });
    }
}
```

### 方式三：带生命周期的插件

```csharp
[Plugin("stateful-strategy", Name = "有状态策略")]
public class StatefulStrategy : PluginStrategyBase, IPluginLifecycle
{
    private string _configJson = "";

    public async Task InitializeAsync(IPluginHost host, CancellationToken ct)
    {
        _configJson = await host.Configuration.LoadConfigurationAsync<string>(Id, ct);
    }

    public Task DisposeAsync()
    {
        // 释放资源
        return Task.CompletedTask;
    }

    public override Task<PluginStrategyResult> ExecuteAsync(
        IPluginWorkspace workspace, CancellationToken ct)
    {
        // 使用 _configJson ...
        return Task.FromResult(new PluginStrategyResult { Success = true });
    }
}
```

---

## Lua 脚本插件

### 运行环境

- 引擎：NLua
- 沙箱限制：已移除 `io`、`os`、`package`、`debug` 库
- 超时：默认 5 秒
- 通过全局变量 `workspace` 访问受限 API

### Lua API

| 方法 | 签名 | 说明 |
|------|------|------|
| `GetUnassignedStudentIds()` | `() → string[]` | 未分配学生的 ID 列表 |
| `GetEmptySeatIds()` | `() → string[]` | 空座位的 ID 列表 |
| `AssignSeat(seatId, studentId)` | `(string, string) → bool` | 分配座位 |
| `GetStudent(studentId)` | `(string) → StudentInfo?` | 查询学生信息 |
| `GetSeat(seatId)` | `(string) → SeatInfo?` | 查询座位信息 |

**StudentInfo 字段：** `Id`、`Name`、`Height`、`NeedsFrontRow`、`FrontRowPreferenceScore`

**SeatInfo 字段：** `Id`、`IsAvailable`、`IsFixed`、`OccupantId`

### 示例 (strategy.lua)

```lua
-- 将身高较高的学生分配到后排（座位 ID 通常越大越靠后）
local unassigned = workspace:GetUnassignedStudentIds()
local emptySeats = workspace:GetEmptySeatIds()

table.sort(unassigned, function(a, b)
    local sa = workspace:GetStudent(a)
    local sb = workspace:GetStudent(b)
    if sa and sb then
        return (sa.Height or 0) > (sb.Height or 0)
    end
    return false
end)

for i = 1, math.min(#unassigned, #emptySeats) do
    workspace:AssignSeat(emptySeats[i], unassigned[i])
end
```

---

## C# 脚本插件

### 运行环境

- 引擎：Roslyn `CSharpScript`
- 程序集白名单：`System.Private.CoreLib`、`System.Linq`、`SeatFlow.Core`、`SeatFlow.Application`
- 命名空间自动导入：`System`、`System.Linq`、`System.Collections.Generic`、`SeatFlow.Core.Workspace`、`SeatFlow.Core.Models`
- 超时：默认 5 秒
- 通过全局变量 `Workspace`（`SeatingWorkspace`，实现了 `IPluginWorkspace`）访问主程序内部 API

### 示例 (strategy.csx)

```csharp
// 为需要前排的学生优先分配座位（示例：按座位 ID 排序选前 N 个可用座位）
var frontSeats = Workspace.FindSeats(s => s.IsAvailable)
    .OrderBy(s => s.Id)
    .Take(Workspace.Students.Count(s => s.NeedsFrontRow))
    .ToList();

var priorityStudents = Workspace.Students
    .Where(s => s.NeedsFrontRow)
    .OrderByDescending(s => s.FrontRowPreferenceScore)
    .ToList();

int count = 0;
for (int i = 0; i < Math.Min(priorityStudents.Count, frontSeats.Count); i++)
{
    if (Workspace.TryAssignSeat(frontSeats[i].Id, priorityStudents[i].Id, out _))
        count++;
}
```

**注意：** C# 脚本可访问 `SeatingWorkspace` 的完整 API（`FindSeats`、`BuildSeatingPlan` 等），不像 Lua 受限。

---

## 配置系统

每个插件可在插件目录下拥有独立的 `config.json`，存放自由格式的 JSON 配置参数。主程序"插件管理"界面提供图形化编辑器。

### 在插件中读取配置

```csharp
// 方式一：每次执行时直接从文件读取
var configPath = Path.Combine(host.PluginDirectory, "config.json");
var config = JsonSerializer.Deserialize<MyConfig>(File.ReadAllText(configPath));

// 方式二：通过 IPluginHost 的 Configuration 服务（需要实现 IPluginLifecycle）
public async Task InitializeAsync(IPluginHost host, CancellationToken ct)
{
    var config = await host.Configuration.LoadConfigurationAsync<MyConfig>(Id, ct);
}
```

---

## .ap-plugin 打包格式

> `.ap-plugin` 是当前推荐的插件分发格式（v1.1+）。旧版 `.apairplugin` 扩展名（v1.0）仍受兼容支持，两种格式均可正常加载。

插件包本质为 ZIP 文件（改后缀名），内部采用扁平结构。

### 格式规范

```
MyPlugin.ap-plugin
├── plugins-manifest.json    (必需 — 包级清单)
├── strategy_a/              (策略子目录)
│   ├── manifest.json        (必需 — 策略元数据)
│   └── StrategyA.dll
├── strategy_b/
│   ├── manifest.json
│   └── strategy.lua
├── data/
│   └── enables.json         (可选)
└── icon.png                 (可选)
```

所有文件位于根目录，策略代码放在对应子目录中。`plugins-manifest.json` 中的 `assembly` 和 `scriptFile` 字段使用相对路径（如 `"strategy_a/StrategyA.dll"`）。

> 旧版单文件格式 `plugin.manifest.json` 已在 v1.1.0 移除，不再可用。

### 使用 PluginPackage 工具类

```csharp
using SeatFlow.Plugins.Sdk.Models;

// 打包插件目录（.ap-plugin 推荐）
PluginPackage.Create("./MyPlugin", "MyPlugin-v1.0.0.ap-plugin");

// 预览包内容（不解包），同时支持 .ap-plugin 和 .apairplugin
var m = await PluginPackage.GetManifestAsync("MyPlugin.ap-plugin");
Console.WriteLine($"{m.Name} v{m.Version} by {m.Author}");

// 验证包结构
var err = await PluginPackage.ValidateAsync("MyPlugin.ap-plugin");
```

### 手动创建

```bash
# Linux/macOS（多策略包）
zip -j MyPlugin.ap-plugin plugins-manifest.json
zip -r MyPlugin.ap-plugin strategy_a/ strategy_b/ data/

# Windows (PowerShell)
Compress-Archive -Path plugins-manifest.json,strategy_a,strategy_b -DestinationPath MyPlugin.zip
Rename-Item MyPlugin.zip MyPlugin.ap-plugin
```

### 安装

将 `.ap-plugin`（或旧版 `.apairplugin`）拖放到 SeatFlow 的"插件管理"页面即可安装，系统自动解包并加载。

---

## 部署

```
SeatFlow/
└── Plugins/                         ← 插件根目录
    └── my-seating-pack/             ← 以包 ID 命名的目录
        ├── plugins-manifest.json    ← 包级清单
        ├── height-desc/             ← 策略子目录
        │   ├── manifest.json        ← 策略元数据
        │   └── HeightStrategy.dll
        ├── front-row-fill/
        │   ├── manifest.json
        │   └── strategy.lua
        └── data/
            └── enables.json
```

> **v1.0 旧格式**（已在 v1.1.0 移除，不可用）：
> ```
> Plugins/
> └── my-strategy/
>     ├── plugin.manifest.json   ← 此格式已不再被识别
>     ├── MyStrategy.dll
>     └── config.json
> ```

**部署方式：**
1. **UI 导入（推荐）**：在"插件管理"页面选择 `.ap-plugin`（或 `.apairplugin`）文件导入
2. **手动部署**：在 `Plugins/` 下创建以包 ID 命名的目录，放入所有文件
3. **安装后**：重启应用或在插件管理页面点击"刷新"

---

## 安全考量

| 插件类型 | 隔离措施 | 风险等级 |
|----------|----------|----------|
| 程序集 DLL | 独立 `AssemblyLoadContext`（`isCollectible: true`），与宿主同权限 | 高 |
| Lua 脚本 | 移除 `io/os/package/debug`，5s 超时，受限 `workspace` API | 低 |
| C# 脚本 | 程序集+命名空间白名单，5s 超时 | 中 |

通用保障：无效清单静默跳过，不含有效清单文件（`plugins-manifest.json` + 策略 `manifest.json`）的目录被忽略。

---

## 故障排除

| 问题 | 可能原因 | 解决方案 |
|------|----------|----------|
| 插件未出现在列表中 | 清单缺失或无效 | 检查 `plugins-manifest.json` 和策略 `manifest.json` 是否存在且为有效 JSON |
| 插件加载失败 | `assembly`/`type` 字段不匹配 | 确保 `type` 是完整命名空间限定名，`assembly` 路径正确 |
| 脚本执行无效果 | 优先级过高或未启用 | 检查 `priority`（越大越先）和 `enabled`（包级 + 策略级） |
| Lua 脚本报错 | 调用了不可用的库 | `io/os/package/debug` 已移除，仅用 `workspace` API |
| C# 脚本编译失败 | 使用了白名单外的 API | 仅用 `System`、`System.Linq`、`Collection` 等 |
| .ap-plugin 安装失败 | 同 ID 插件包已存在 | 先卸载旧版本再安装 |

---

## 扩展路线图

### 数据提供者插件（中期）

将实现 `IPluginDataProvider : IPlugin`（`Category = "provider"`）：

```csharp
// 未来接口（当前不实现）
public interface IPluginDataProvider : IPlugin
{
    Task<List<Student>> LoadStudentsAsync(string? connectionString, CancellationToken ct);
}
```

安装后自动出现在学生数据源下拉框中。PluginManager 通过 `LoadPlugins("provider")` 加载。

### 导出器插件（中期）

将实现 `IPluginExporter : IPlugin`（`Category = "exporter"`）：

```csharp
// 未来接口（当前不实现）
public interface IPluginExporter : IPlugin
{
    Task ExportAsync(SeatingPlan plan, Stream output, CancellationToken ct);
}
```

安装后自动出现在导出格式下拉框中。PluginManager 通过 `LoadPlugins("exporter")` 加载。

### 策略能力声明

策略可通过 manifest 声明**能力**来使用受保护的操作（如标记固定座位）：

**Manifest 声明**（`manifest.json`）：
```json
{
    "id": "MyPlugin",
    "capabilities": ["MarkFixedSeat"],
    ...
}
```

**运行时调用**（通过 `IPluginWorkspace`）：
```csharp
public async Task<PluginStrategyResult> ExecuteAsync(IPluginWorkspace workspace, CancellationToken ct)
{
    // 标记座位为固定（需在 manifest 中声明 "MarkFixedSeat" 能力）
    if (workspace.TryMarkFixed("seat-1", "student-A", Id, Name, out var error))
    {
        // 座位已锁定，后续策略（如碎片整理 Defrag）不会移动此座位
    }
    else
    {
        // 未声明能力 → error 包含 "未声明 MarkFixedSeat 能力"
    }
}
```

**可用能力**（定义在 `SeatFlow.Core.Strategies.Capability`）：
| 常量 | 接口方法 | 说明 |
|------|----------|------|
| `MarkFixedSeat` | `IPluginWorkspace.TryMarkFixed()` | 标记座位为固定，设置 `IsFixed=true`。被保护座位不会被 `GetEmptySeats()` 返回，不会被碎片整理策略移动 |

未声明能力时调用会被拒绝（返回 false）并记录警告日志。

### 如何为新类别扩展

`Category` 是自由格式字符串。添加新类别时：
1. 在 `SeatFlow.Contracts.Interfaces` 中定义 `IPluginXxx : IPlugin` 接口
2. 在 SDK 中新建 `PluginXxxBase : PluginBase, IPluginXxx` 基类
3. 在 `PluginManager.LoadPlugins()` 的 switch 中添加 `"xxx"` case
4. 在 UI 层添加相应的加载和使用逻辑

---

## 参考

- [SeatFlow.Contracts](../SeatFlow.Contracts/) — `IPlugin`、`IPluginSeatingStrategy` 等接口
- [SeatFlow.Core](../SeatFlow.Core/) — 核心模型（Student、Seat、SeatingWorkspace）
- [项目设计文档](../ARCHITECTURE.md) — 架构总览
- [实现阶段规划](../Phases.md) — 各阶段详情
