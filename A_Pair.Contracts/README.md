# A_Pair.Contracts

定义系统各层及插件间的共享契约（接口、DTO、抽象类型）。插件 SDK 仅依赖此层，不依赖 Core。

## 主要接口

### IPlugin
所有插件类型的通用身份契约，提供 `Id`、`Name`、`Version`、`Category` 元数据。

### IPluginSeatingStrategy : IPlugin
排座策略插件接口。插件实现 `ExecuteAsync(IPluginWorkspace, CancellationToken)` 方法定义自定义排座逻辑。
- `Priority` — 执行优先级，数值越小越先执行
- `IsEnabled` — 是否启用
- `Category` / `Version` — 默认接口方法返回 `"strategy"` / `"1.0.0"`

### IPluginLifecycle
可选实现。提供 `InitializeAsync(IPluginHost, CancellationToken)` 和 `DisposeAsync()`，宿主在加载/卸载时调用。

### IPluginHost
插件宿主，在 `InitializeAsync` 时传递给插件，提供 `Configuration` 服务和 `PluginDirectory` 路径。

### IPluginConfigurationService
插件配置读写服务，支持 `LoadConfigurationAsync` / `SaveConfigurationAsync`。

## 模型

### PluginStrategyResult
排座策略执行结果，包含 `Success` 和 `Message`。

### IPluginWorkspace
插件视角的工作区契约，暴露插件所需的受限 API：`Students`、`TryAssignSeat`、`GetEmptySeats`、`FindSeats`、`GetAssignments`。Core 层的 `SeatingWorkspace` 实现此接口。

### IPluginStudent / IPluginSeat
学生和座位的只读视图接口，仅暴露插件所需字段。

## 目录结构

```
A_Pair.Contracts/
  Interfaces/     — 纯接口定义
  Models/         — DTO、结果类型、视图接口
```

## 示例

```csharp
public class MyStrategy : IPluginSeatingStrategy
{
    public string Id => "my-strategy";
    public string Name => "My Strategy";
    public int Priority { get; set; } = 50;
    public bool IsEnabled { get; set; } = true;

    public Task<PluginStrategyResult> ExecuteAsync(IPluginWorkspace workspace, CancellationToken ct)
    {
        var emptySeats = workspace.GetEmptySeats().ToList();
        // 自定义排座逻辑...
        return Task.FromResult(new PluginStrategyResult { Success = true });
    }
}
```
