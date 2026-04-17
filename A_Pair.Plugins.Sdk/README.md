# A_Pair.Plugins.Sdk

## 项目简介

A_Pair.Plugins.Sdk 是座位安排系统的插件开发 SDK，定义了插件开发所需的抽象接口、元数据特性、通用模型等，供外部插件项目引用，实现自定义排座策略、数据处理等扩展能力。

---

## 目录结构

- **Abstractions/** 插件抽象接口和基类
- **Attributes/** 插件元数据特性（如 [Plugin]）
- **Models/** 插件通用数据模型

---

## 主要接口与类型

### 1. IPluginSeatingStrategy
- 继承自 ISeatingStrategy（A_Pair.Contracts）
- 用途：插件实现自定义排座策略的标准接口
- 主要成员：
  - `string Id` 策略唯一标识
  - `string Name` 策略名称
  - `int Priority` 执行优先级（数值越小越先执行）
  - `bool IsEnabled` 是否启用
  - `Task<StrategyExecutionResult> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken)` 执行策略
  - `ValidationResult ValidateConfiguration()` 配置校验
- 典型用法：实现该接口并在插件清单中声明，系统自动发现并加载

### 2. 插件元数据特性（Attributes）
- `[Plugin]`：标记插件主类，包含 Id、名称、描述、作者、版本等元数据
- 用途：插件发现与元数据读取

### 3. 通用模型（Models）
- 提供插件开发常用的数据结构，如策略配置、运行时上下文等

---

## 对外功能/接口

- 插件项目需引用本 SDK，并实现 `IPluginSeatingStrategy` 或其他扩展接口
- 支持通过元数据特性声明插件信息
- 插件可访问 SeatingWorkspace、Student、Seat 等核心模型（由 Contracts/Core 提供）
- 支持插件配置热重载、独立存储

---

## 插件开发流程

1. 新建类库项目，引用 A_Pair.Plugins.Sdk、A_Pair.Contracts
2. 实现 `IPluginSeatingStrategy`，并用 `[Plugin]` 标记主类
3. 在插件目录下添加 `plugin.manifest.json`，声明入口类型、优先级等
4. 编译生成 DLL，放入主程序 Plugins 目录
5. 主程序自动发现并加载插件

---

## 示例代码

```csharp
using A_Pair.Plugins.Sdk.Attributes;
using A_Pair.Contracts.Interfaces;

[Plugin("MyStrategy", Name = "自定义策略", Version = "1.0", Author = "张三")]
public class MyStrategy : IPluginSeatingStrategy
{
    public string Id => "MyStrategy";
    public string Name => "自定义策略";
    public int Priority { get; set; } = 50;
    public bool IsEnabled { get; set; } = true;

    public Task<StrategyExecutionResult> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken)
    {
        // 实现自定义排座逻辑
    }

    public ValidationResult ValidateConfiguration()
    {
        // 校验配置
    }
}
```

---

## 插件清单文件（plugin.manifest.json）示例

```json
{
  "id": "MyStrategy",
  "type": "strategy",
  "entry": "MyNamespace.MyStrategy, MyPluginAssembly",
  "priority": 50,
  "version": "1.0.0",
  "author": "张三",
  "description": "一个自定义排座策略插件"
}
```

---

## 典型场景
- 新增排座策略（如分组优先、特殊需求优先等）
- 数据导入/导出扩展
- UI 插件（如自定义配置面板）

---

## 参考
- [A_Pair.Contracts](../A_Pair.Contracts/README.md)：插件契约接口
- [A_Pair.Core](../A_Pair.Core/README.md)：核心模型

---

## 维护者
- 项目主页：https://github.com/your-org/A_Pair
- 联系方式：xxx@yourdomain.com