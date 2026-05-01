# Task ID: 2

**Title:** 主窗口导航框架

**Status:** done

**Dependencies:** 1 ✓

**Priority:** high

**Description:** 实现 MainWindow 的导航框架，支持在 8 个功能视图间切换

**Details:**

1. 设计导航结构：侧边栏 + ContentControl 内容区
2. MainShellViewModel 管理 CurrentViewModel 属性和 NavigateCommand
3. 创建 8 个 ViewModel 的空壳
4. ViewLocator 自动解析并显示对应的 View
5. FluentIcons.Avalonia 图标库集成
6. 侧边栏折叠/展开功能
7. 设置和关于按钮置底

**Test Strategy:**

启动应用，点击各导航项验证视图切换正常
