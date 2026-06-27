# ADR-003: 采用分层架构 + 插件化扩展设计

## 状态
已接受

## 日期
2025-12（项目启动时）

## 背景
项目需要满足以下约束：

- 核心业务逻辑（座位安排策略）与 UI 和使用者基础设施解耦
- 第三方开发者可以编写自定义座位策略插件
- 支持多种数据源（CSV、Excel、JSON）和导出格式（Excel、PDF、CSV、图片）
- 易于测试 — 每层可独立进行单元测试

## 决策
采用经典三层架构 + 独立 Contracts 程序集 + 插件 SDK：

```
Presentation.Avalonia → Application → Core / Contracts / Infrastructure
                                       Plugins.Sdk (外部)
```

- **Core** 层零外部依赖（仅 .NET BCL），包含实体、值对象、策略接口、领域服务
- **Contracts** 层定义跨层共享接口（与 Core 分离，以避免循环依赖和减少插件引用的表面积）
- **Application** 层编排业务逻辑：执行管道、外观模式、命令历史、插件管理
- **Infrastructure** 层实现所有外部交互：数据提供者、导出器、布局构建器、仓储
- **Plugins.Sdk** 是轻量程序集，仅包含插件作者需要的契约类型

## 考虑的替代方案

### 单体项目（无分层）
- 优点：简单，无项目引用复杂度
- 缺点：耦合严重，无法独立测试；无法支持插件化扩展；编译时间长
- 拒绝：不满足可扩展性和可测试性需求

### Clean Architecture / Onion Architecture
- 优点：严格的依赖反转，Core 完全不依赖任何外层
- 缺点：过度抽象（`IEntity`、`IRepository<T>` 等泛型接口），对于桌面应用而言过于复杂
- 拒绝：项目规模不需要完全的洋葱架构。三层 + Contracts 分离已经提供足够的依赖反转

### 微服务架构
- 优点：独立部署、独立扩展
- 缺点：完全不适用于桌面应用
- 拒绝：这是桌面端系统，不涉及服务端部署

## 后果
- DI 注册集中在 `ServiceCollectionExtensions.AddSeatFlowApplication()`（Application 层）和 `Program.cs`（Presentation 层）
- 插件通过 `AssemblyLoadContext` 隔离加载，每个插件拥有独立的加载上下文
- `IApplicationFacade` 是 UI 层与业务逻辑的唯一接触点，外观模式隐藏内部复杂度
- Contracts 层保持最小面积：仅包含插件作者和跨层交互需要的接口
- 测试分为三个独立项目（Core.Tests、Application.Tests、Infrastructure.Tests），对应三层
