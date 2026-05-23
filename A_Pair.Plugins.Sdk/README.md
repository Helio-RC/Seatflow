# A_Pair.Plugins.Sdk — 插件开发 SDK

A_Pair 座位安排系统的插件开发工具包。引用此 SDK 即可开发自定义排座策略插件，无需依赖主程序集。

---

## 插件架构概述

插件系统采用分层接口设计，从通用到具体：

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

### 插件加载方式

| 类型 | 载体 | 适用场景 |
|------|------|----------|
| **程序集插件** | 编译后的 `.dll` 文件 | 复杂算法、需要引用外部库 |
| **Lua 脚本插件** | `.lua` 文本文件 | 快速原型、简单逻辑、非 .NET 开发者 |
| **C# 脚本插件** | `.csx` 文本文件 | 需要 .NET 标准库但不想管理编译流程 |

所有插件统一通过 `plugin.manifest.json` 发现和配置，统一实现 `IPlugin` 接口。

---

## 快速开始

### 1. 创建插件项目

```bash
dotnet new classlib -n MyPlugin
cd MyPlugin
dotnet add reference /path/to/A_Pair/A_Pair.Plugins.Sdk/A_Pair.Plugins.Sdk.csproj
```

### 2. 实现策略

```csharp
using A_Pair.Contracts.Interfaces;
using A_Pair.Core.Workspace;

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
        SeatingWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var emptySeats = workspace.GetEmptySeats().ToList();
        var unassigned = workspace.Students
            .Where(s => !workspace.BuildSeatingPlan().Assignments.Values.Contains(s.Id))
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

在项目根目录创建 `plugin.manifest.json`：

```json
{
  "id": "my-first-plugin",
  "name": "我的第一个插件",
  "version": "1.0.0",
  "category": "strategy",
  "description": "一个简单的排座策略示例",
  "author": "Your Name",
  "assembly": "MyPlugin.dll",
  "type": "MyPlugin.MyStrategy",
  "priority": 50,
  "enabled": true
}
```

### 4. 构建并部署

```bash
dotnet build -c Release
# 将 bin/Release/net10.0/MyPlugin.dll 和 plugin.manifest.json
# 复制到 A_Pair 的 plugins/MyPlugin/ 目录下
```

---

## 项目结构

### SDK 提供的内容

| 路径 | 类型 | 用途 |
|------|------|------|
| `Attributes/PluginAttribute.cs` | `[Plugin]` 特性 | 可选的类级别元数据注解，字段与 PluginManifest 对应 |
| `Abstractions/PluginBase.cs` | 抽象基类 | 实现 IPlugin 身份元数据的基类，减少样板代码 |
| `Abstractions/PluginStrategyBase.cs` | 抽象基类 | 继承 PluginBase，扩展策略执行相关成员 |
| `Models/PluginPackage.cs` | 静态工具类 | `.apairplugin` 打包/解包/验证 |

### 通过传递引用可用的类型

SDK 引用了 `A_Pair.Contracts` 和 `A_Pair.Core`，以下类型可直接使用：

| 类型 | 命名空间 | 来源 |
|------|----------|------|
| `IPlugin` | `A_Pair.Contracts.Interfaces` | Contracts |
| `IPluginLifecycle` | `A_Pair.Contracts.Interfaces` | Contracts |
| `IPluginHost` | `A_Pair.Contracts.Interfaces` | Contracts |
| `IPluginSeatingStrategy` | `A_Pair.Contracts.Interfaces` | Contracts |
| `PluginStrategyResult` | `A_Pair.Contracts.Interfaces` | Contracts |
| `IPluginConfigurationService` | `A_Pair.Contracts.Interfaces` | Contracts |
| `SeatingWorkspace` | `A_Pair.Core.Workspace` | Core |
| `SeatingPlan` | `A_Pair.Core.Workspace` | Core |
| `Student` | `A_Pair.Core.Models` | Core |
| `Seat` / `GridSeat` / `PolarSeat` | `A_Pair.Core.Models` | Core |
| `AttributeBag` | `A_Pair.Core.Utilities` | Core |

---

## 接口参考

### IPlugin（基础身份）

所有插件的通用身份契约：

```csharp
namespace A_Pair.Contracts.Interfaces;

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
namespace A_Pair.Contracts.Interfaces;

public interface IPluginSeatingStrategy : IPlugin
{
    string IPlugin.Category => "strategy";   // 默认类别
    string IPlugin.Version => "1.0.0";       // 默认版本

    int Priority { get; set; }               // 执行优先级（数值越小越先执行）
    bool IsEnabled { get; set; }             // 是否启用

    Task<PluginStrategyResult> ExecuteAsync(
        SeatingWorkspace workspace,
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
namespace A_Pair.Plugins.Sdk.Abstractions;

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
        SeatingWorkspace workspace, CancellationToken ct);
}
```

**继承链示意：**
```
PluginBase (I: Id, Name, Version, Category)
  └── PluginStrategyBase (+ Priority, IsEnabled, + abstract ExecuteAsync)
```

### PluginAttribute

声明式元数据注解，字段与 `PluginManifest` 完全对应：

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

## plugin.manifest.json 规范

每个插件目录必须包含此文件，是插件发现的主要机制。

### 字段说明

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|------|------|------|--------|------|
| `id` | string | **是** | — | 插件唯一标识符 |
| `name` | string | 否 | `""` | 插件显示名称 |
| `version` | string | 否 | `"1.0.0"` | 语义化版本号 |
| `category` | string | 否 | `"strategy"` | 功能类别，用于路由加载 |
| `description` | string | 否 | `""` | 插件功能描述 |
| `author` | string | 否 | `""` | 插件作者 |
| `assembly` | string | 见注 | `""` | DLL 文件名（程序集插件） |
| `type` | string | 见注 | `""` | 入口类型的完全限定名（程序集插件） |
| `priority` | int | 否 | `50` | 执行优先级，越小越先执行 |
| `enabled` | bool | 否 | `true` | 加载时是否默认启用 |
| `dependencies` | string[] | 否 | `[]` | 依赖的插件 ID 列表 |
| `scriptFile` | string | 见注 | null | 脚本文件名（脚本插件） |
| `scriptType` | string | 见注 | null | 脚本类型：`"lua"` 或 `"csharp"` |

> **注：** 程序集插件需同时提供 `assembly` + `type`；脚本插件需同时提供 `scriptFile` + `scriptType`。两者互斥。

### 程序集插件示例

```json
{
  "id": "height-strategy",
  "name": "身高优先排座",
  "version": "1.0.0",
  "category": "strategy",
  "description": "按学生身高降序分配座位，高个子坐后排",
  "author": "张三",
  "assembly": "HeightStrategy.dll",
  "type": "MyPlugin.HeightStrategy",
  "priority": 30,
  "enabled": true
}
```

### Lua 脚本插件示例

```json
{
  "id": "lua-random-fill",
  "name": "Lua 随机填充",
  "version": "1.0.0",
  "category": "strategy",
  "description": "用 Lua 实现随机分配剩余座位",
  "author": "李四",
  "priority": 60,
  "enabled": true,
  "scriptFile": "strategy.lua",
  "scriptType": "lua"
}
```

### C# 脚本插件示例

```json
{
  "id": "cs-front-row-rotate",
  "name": "前排轮换策略",
  "version": "1.0.0",
  "category": "strategy",
  "description": "用 C# 脚本实现自定义前排轮换逻辑",
  "author": "王五",
  "priority": 40,
  "enabled": true,
  "scriptFile": "strategy.csx",
  "scriptType": "csharp"
}
```

---

## SeatingWorkspace API 参考

`ExecuteAsync` 接收一个 `SeatingWorkspace` 实例，包含所有学生和座位数据。

### 属性

| 成员 | 类型 | 说明 |
|------|------|------|
| `Students` | `IReadOnlyList<Student>` | 所有待分配学生（只读） |

### 方法

**`bool TryAssignSeat(string seatId, string studentId, out string error)`**

将学生分配到座位。验证逻辑：
- 座位和学生必须存在
- 座位必须可用（`IsAvailable == true`）
- 固定座位只能分配给指定学生
- 同一学生不可已分配到其他座位

成功时自动更新学生座位历史记录。

**`IEnumerable<Seat> GetEmptySeats()`**

返回所有可用且非固定的空座位。

**`IEnumerable<Seat> FindSeats(Func<Seat, bool> predicate)`**

按条件查找座位。示例：
```csharp
var frontRow = workspace.FindSeats(s => s is GridSeat g && g.Row == 1);
var groupA = workspace.FindSeats(s => s.LogicalGroup == "A组" && s.IsAvailable);
```

**`SeatingPlan BuildSeatingPlan()`**

构建当前分配状态的只读快照：
```csharp
public class SeatingPlan
{
    public Dictionary<string, string> Assignments { get; set; }
    // Key = 座位 ID, Value = 学生 ID
}
```

**`void ApplySnapshotAssignments(Dictionary<string, string> seatAssignments)`**

清空当前所有分配并按给定快照恢复。

---

## 数据模型参考

### Student

| 属性 | 类型 | 说明 |
|------|------|------|
| `Id` | `string` | 唯一标识符（GUID） |
| `Name` | `string` | 学生姓名 |
| `Height` | `float?` | 身高（厘米），可为 null |
| `Gender` | `Gender?` | 性别，可为 null |
| `NeedsFrontRow` | `bool` | 是否需要前排座位 |
| `FrontRowPreferenceScore` | `int` | 前排偏好分数，越大越优先 |
| `Extensions` | `AttributeBag` | 扩展数据挂载点 |

### Seat（抽象基类）

| 属性 | 类型 | 说明 |
|------|------|------|
| `Id` | `string` | 唯一标识符（GUID） |
| `Type` | `SeatType` | 座位类型：`Grid` / `Polar` / `Freeform` |
| `LogicalGroup` | `string` | 逻辑分组（如 "A区"） |
| `IsAvailable` | `bool` | 是否可用 |
| `IsFixed` | `bool` | 是否固定座位 |
| `OccupantId` | `string?` | 当前占用学生 ID，null 表示空 |
| `Extensions` | `AttributeBag` | 扩展数据挂载点 |

### GridSeat : Seat

| 附加属性 | 类型 | 说明 |
|----------|------|------|
| `Row` | `int` | 行号（1-based） |
| `Column` | `int` | 列号（1-based） |

### PolarSeat : Seat

| 附加属性 | 类型 | 说明 |
|----------|------|------|
| `Ring` | `int` | 环号（1-based，1=最内环） |
| `Radius` | `double` | 半径距离 |
| `AngleDegrees` | `double` | 角度（0°=右侧水平，逆时针） |

### AttributeBag

线程安全的键值对容器，用于挂载自定义数据：

```csharp
student.Extensions.Set("customScore", 100);
if (student.Extensions.TryGet<int>("customScore", out var score))
    Console.WriteLine(score);
foreach (var kv in seat.Extensions.GetAll())
    Console.WriteLine($"{kv.Key} = {kv.Value}");
```

---

## 程序集插件完整示例

### 方式一：继承 PluginStrategyBase（推荐，最少代码）

```csharp
using A_Pair.Plugins.Sdk.Attributes;
using A_Pair.Plugins.Sdk.Abstractions;

[Plugin("height-desc", Name = "身高降序分配",
    Description = "按身高从高到低分配座位", Author = "张三", Priority = 25)]
public class HeightDescendingStrategy : PluginStrategyBase
{
    // Id, Name, Version, Category 从 PluginBase 继承，自动从 [Plugin] 读取
    // Priority, IsEnabled 自动从 [Plugin] 读取
    // 只需实现 ExecuteAsync！

    public override Task<PluginStrategyResult> ExecuteAsync(
        SeatingWorkspace workspace, CancellationToken ct)
    {
        var assigned = workspace.BuildSeatingPlan().Assignments.Values;
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
using A_Pair.Contracts.Interfaces;
using A_Pair.Core.Workspace;
using A_Pair.Core.Models;

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
        SeatingWorkspace workspace, CancellationToken ct)
    {
        var needsFront = workspace.Students
            .Where(s => s.NeedsFrontRow)
            .OrderByDescending(s => s.FrontRowPreferenceScore)
            .ToList();

        var frontSeats = workspace.FindSeats(s =>
            s is GridSeat g && g.Row == 1 && s.IsAvailable).ToList();

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
        SeatingWorkspace workspace, CancellationToken ct)
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
- 程序集白名单：`System.Private.CoreLib`、`System.Linq`、`A_Pair.Core`、`A_Pair.Application`
- 命名空间自动导入：`System`、`System.Linq`、`System.Collections.Generic`、`A_Pair.Core.Workspace`、`A_Pair.Core.Models`
- 超时：默认 5 秒
- 通过全局变量 `Workspace`（类型为 `SeatingWorkspace`）访问**完整公共 API**

### 示例 (strategy.csx)

```csharp
// 为需要前排的学生优先分配第一排座位
var frontSeats = Workspace.FindSeats(s =>
    s is GridSeat g && g.Row == 1)
    .OrderBy(s => s.Id)
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

## .apairplugin 打包格式

`.apairplugin` 是统一的插件分发格式，本质为 ZIP 文件（改后缀名），内部采用扁平结构。

### 格式规范

```
MyPlugin.apairplugin
├── plugin.manifest.json    (必需)
├── MyPlugin.dll            (或 .lua / .csx)
├── config.json             (可选)
└── icon.png                (可选)
```

所有文件位于根目录，**不含子文件夹**。manifest 中的 `assembly` 和 `scriptFile` 字段应使用纯文件名。

### 使用 PluginPackage 工具类

```csharp
using A_Pair.Plugins.Sdk.Models;

// 打包插件目录
PluginPackage.Create("./MyPlugin", "MyPlugin-v1.0.0.apairplugin");

// 预览包内容（不解包）
var m = await PluginPackage.GetManifestAsync("MyPlugin.apairplugin");
Console.WriteLine($"{m.Name} v{m.Version} by {m.Author}");

// 验证包结构
var err = await PluginPackage.ValidateAsync("MyPlugin.apairplugin");
```

### 手动创建

```bash
# Linux/macOS
zip -j MyPlugin.apairplugin plugin.manifest.json MyPlugin.dll config.json

# Windows (PowerShell)
Compress-Archive -Path plugin.manifest.json,MyPlugin.dll -DestinationPath MyPlugin.zip
Rename-Item MyPlugin.zip MyPlugin.apairplugin
```

### 安装

将 `.apairplugin` 拖放到 A_Pair 的"插件管理"页面即可安装，系统自动解包并加载。

---

## 部署

```
A_Pair/
└── Plugins/                    ← 插件根目录
    └── my-custom-strategy/     ← 以插件 ID 命名的子目录
        ├── plugin.manifest.json
        ├── MyPlugin.dll
        ├── config.json         (可选)
        └── icon.png            (可选)
```

**部署方式：**
1. **UI 导入（推荐）**：在"插件管理"页面选择 `.apairplugin` 文件导入
2. **手动部署**：在 `Plugins/` 下创建以插件 ID 命名的目录，放入所有文件
3. **安装后**：重启应用或在插件管理页面点击"刷新"

---

## 安全考量

| 插件类型 | 隔离措施 | 风险等级 |
|----------|----------|----------|
| 程序集 DLL | 独立 `AssemblyLoadContext`（`isCollectible: true`），与宿主同权限 | 高 |
| Lua 脚本 | 移除 `io/os/package/debug`，5s 超时，受限 `workspace` API | 低 |
| C# 脚本 | 程序集+命名空间白名单，5s 超时 | 中 |

通用保障：无效清单静默跳过，不含 `plugin.manifest.json` 的目录被忽略。

---

## 故障排除

| 问题 | 可能原因 | 解决方案 |
|------|----------|----------|
| 插件未出现在列表中 | 清单缺失或无效 | 检查 `plugin.manifest.json` 是否存在且为有效 JSON |
| 插件加载失败 | `assembly`/`type` 字段不匹配 | 确保 `type` 是完整命名空间限定名 |
| 脚本执行无效果 | 优先级过高或未启用 | 检查 `priority`（越小越先）和 `enabled` |
| Lua 脚本报错 | 调用了不可用的库 | `io/os/package/debug` 已移除，仅用 `workspace` API |
| C# 脚本编译失败 | 使用了白名单外的 API | 仅用 `System`、`System.Linq`、`Collection` 等 |
| .apairplugin 安装失败 | 同 ID 插件已存在 | 先卸载旧版本再安装 |

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

### 如何为新类别扩展

`Category` 是自由格式字符串。添加新类别时：
1. 在 `A_Pair.Contracts.Interfaces` 中定义 `IPluginXxx : IPlugin` 接口
2. 在 SDK 中新建 `PluginXxxBase : PluginBase, IPluginXxx` 基类
3. 在 `PluginManager.LoadPlugins()` 的 switch 中添加 `"xxx"` case
4. 在 UI 层添加相应的加载和使用逻辑

---

## 参考

- [A_Pair.Contracts](../A_Pair.Contracts/) — `IPlugin`、`IPluginSeatingStrategy` 等接口
- [A_Pair.Core](../A_Pair.Core/) — 核心模型（Student、Seat、SeatingWorkspace）
- [项目设计文档](../Goal.md) — 架构总览
- [实现阶段规划](../Phases.md) — 各阶段详情
