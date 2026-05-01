# A_Pair 表示层（UI层）实现需求文档

## 背景

后端（Core、Application、Infrastructure）已完成且所有测试通过。表示层 A_Pair.Presentation.Avalonia 处于早期开发阶段：
- Program.cs 无 DI 容器配置，无到其他 A_Pair 项目的项目引用
- App.axaml.cs 直接 new MainWindow 和 MainWindowViewModel，未使用 DI
- 存在 6 个 View 代码后置桩（空壳），但没有对应的 .axaml 文件
- MainWindow 目前只显示一个占位 Greeting 文本
- ViewLocator 已实现（XXXViewModel → XXXView 类型名替换）

## 目标

完成 Avalonia 12 桌面 UI 的全部功能实现，使其成为功能完整的座位安排与轮换系统前端。

## 具体需求

### 1. 基础设施搭建（DI 接入与导航）
- Presentation.Avalonia 项目添加对 Application 项目的引用
- Program.cs 中配置 Microsoft.Extensions.DependencyInjection 容器，调用 services.AddA_PairApplication()
- App.axaml.cs 中通过 DI 解析 MainWindowViewModel
- 实现主窗口导航框架：侧边栏或 Tab 切换 6 个功能视图
- MainWindowViewModel 管理当前激活的 ViewModel 和导航状态

### 2. 数据管理视图（DataManagementView）
- 创建 DataManagementView.axaml 界面文件
- 实现 DataManagementViewModel：学生数据导入（CSV/JSON/XLSX 文件选择）、数据预览表格、导出功能
- 集成文件对话框服务（打开/保存）
- 导入验证错误展示

### 3. 会场配置视图（VenueConfigurationView）
- 创建 VenueConfigurationView.axaml 界面文件
- 实现 VenueConfigurationViewModel：会场列表管理（增删改）、布局类型选择（Grid/Polar/Freeform）
- 布局参数编辑面板（行列数、半径、角度等，根据布局类型动态切换）
- 会场布局可视化预览（Canvas 绘制座位位置）
- 障碍物管理（添加/移除讲台、柱子等）

### 4. 策略配置视图（StrategyConfigurationView）
- 创建 StrategyConfigurationView.axaml 界面文件
- 实现 StrategyConfigurationViewModel：策略列表展示（含内置和插件策略）、启用/禁用切换、优先级调整（上下移动）
- 策略配置参数编辑（每个策略的配置面板动态生成）
- 策略验证结果展示

### 5. 座位安排视图（SeatingArrangementView）— 核心视图
- 创建 SeatingArrangementView.axaml 界面文件
- 实现 SeatingArrangementViewModel：调用 IApplicationFacade.GenerateSeatingAsync 生成座位
- 座位图可视化渲染（Canvas 上用不同颜色/形状表示座位状态：空位、已分配、固定）
- 学生列表侧边栏（未分配学生、已分配学生）
- 拖拽换位交互（拖拽学生到座位、两个座位间交换）
- 缩放与平移（ScrollViewer + 变换）
- 座位 Tooltip（悬停显示学生详情）
- 进度报告展示（生成过程中的进度条和状态信息）

### 6. 快照历史视图（SnapshotHistoryView）
- 创建 SnapshotHistoryView.axaml 界面文件
- 实现 SnapshotHistoryViewModel：快照列表（按时间倒序）、快照详情预览
- 回滚到指定快照（带确认对话框）
- 快照对比（当前 vs 历史）

### 7. 插件管理视图（PluginManagementView）
- 创建 PluginManagementView.axaml 界面文件
- 实现 PluginManagementViewModel：已发现插件列表、插件启用/禁用、插件配置编辑
- 脚本编辑器（Lua/C# 脚本插件的文本编辑，含基础语法高亮）
- 插件状态展示（加载成功/失败/已禁用）

### 8. 撤销/重做与全局功能
- UI 层绑定撤销/重做（Ctrl+Z / Ctrl+Y 快捷键和工具栏按钮）
- 全局错误处理和用户提示（异常时的友好对话框）
- 菜单栏/工具栏：新建、保存、导出、设置等常用操作入口
- 应用程序设置视图（AppSettings 编辑）

### 9. 国际化和本地化
- 整理 .resx 资源文件
- 所有 UI 文本通过 IStringLocalizer 获取
- 支持中英文切换（默认中文）
