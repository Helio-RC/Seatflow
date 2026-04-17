# A_Pair.Presentation.Avalonia

## 项目简介
表示层，基于 Avalonia UI，负责桌面端界面与用户交互。

## 主要 ViewModel/服务
- MainWindowViewModel：主窗口
- MainShellViewModel：主壳体
- 各功能区 ViewModel：数据管理、策略配置、快照、插件、会场等

## 对外接口
- 通过依赖注入获取 IApplicationFacade
- 绑定命令与数据到 UI

## 示例
```csharp
public MainWindowViewModel(IApplicationFacade facade) { ... }
```