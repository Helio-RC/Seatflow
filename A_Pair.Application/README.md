# A_Pair.Application

## 项目简介
应用层，负责编排数据加载、布局构建、策略执行流程，提供外观接口供 UI/CLI 调用。

## 主要接口与服务

### IApplicationFacade
- 用途：UI/CLI 统一入口，封装所有业务操作。
- 主要方法：
  - LoadConfigurationAsync
  - LoadStudentsAsync
  - GenerateSeatingAsync
  - ExportSeatingPlanAsync
  - GetSnapshotsAsync
  - RollbackToSnapshotAsync

### StrategyExecutionPipeline
- 用途：按优先级依次执行策略，生成最终座位表。

### PluginManager
- 用途：插件发现、加载、卸载。

## 对外功能
- 通过依赖注入暴露 IApplicationFacade。
- 支持插件热加载、脚本策略。

## 示例
```csharp
var plan = await facade.GenerateSeatingAsync(request, progress);
```