# ADR-002: 选择 CommunityToolkit.Mvvm 作为 MVVM 框架

## 状态
已接受

## 日期
2025-12（项目启动时）

## 背景
项目使用 MVVM 模式组织 UI 层。需要选择一个 MVVM 框架来提供：

- 属性变更通知（`INotifyPropertyChanged`）
- 命令绑定（`ICommand`）
- 视图-视图模型自动定位
- 最小化样板代码

## 决策
使用 **CommunityToolkit.Mvvm 8.4** 作为 MVVM 框架。

## 考虑的替代方案

### ReactiveUI
- 优点：功能丰富的响应式编程框架，支持 `WhenAnyValue` 等高级操作符
- 缺点：学习曲线陡峭；响应式范式与简单 CRUD 操作不匹配；依赖 `System.Reactive`，增加包体积和复杂度
- 拒绝：项目不需要复杂的响应式管道（如节流、合并观察流）。CommunityToolkit.Mvvm 的源代码生成器更轻量且足够

### Prism
- 优点：完整的企业级 MVVM 框架，包含区域导航、模块化、对话框服务
- 缺点：沉重；与 Avalonia 的深度集成需要额外适配；项目不需要区域导航或模块化 UI
- 拒绝：Prism 的体量远超项目需求，引入它会增加不必要的抽象层

### 手动实现 INotifyPropertyChanged
- 优点：零依赖，完全控制
- 缺点：需要手写大量样板代码（属性、命令、变更通知）；容易出错（属性名拼写错误）
- 拒绝：```[ObservableProperty]``` 和 `[RelayCommand]` 源代码生成器消除了数百行重复代码

## 后果
- `[ObservableProperty]` 从私有字段自动生成公共属性 + 变更通知 + `On<PropertyName>Changed` 分部方法钩子
- `[RelayCommand]` 从方法自动生成 `ICommand` 属性
- `ViewModelBase`（继承 `ObservableObject`）是所有视图模型的基类，提供 `SafeExecuteAsync` 等公共方法
- 源代码生成器在编译时运行，无运行时性能开销
- 使用 `IMessenger`（`WeakReferenceMessenger`）进行跨视图模型通信（如导航通知）
