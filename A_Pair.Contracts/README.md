# A_Pair.Contracts

## 项目简介
定义系统各层及插件间的共享契约（接口、DTO、抽象类型）。

## 主要接口

### IPluginSeatingStrategy
- 继承 ISeatingStrategy，插件实现的策略接口。
- 用途：供外部插件实现自定义排座逻辑。

## 对外功能
- 插件开发者需实现本项目接口，系统通过反射加载。

## 示例
```csharp
public class MyStrategy : IPluginSeatingStrategy { ... }
```