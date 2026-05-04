# Task ID: 13

**Title:** 设置视图（SettingsView）

**Status:** done

**Dependencies:** 2 ✓

**Priority:** low

**Description:** 实现应用程序设置页面，支持主题切换、语言、数据目录和页面切换动画等配置项

**Details:**

1. SettingsViewModel 注入 IApplicationFacade/IDialogService，启动时加载 AppSettings
2. 外观：主题模式按钮（跟随系统/浅色/深色），即时设置 Application.RequestedThemeVariant
3. 语言：界面语言代码输入框（PlaceholderText="留空跟随系统"）
4. 存储：数据目录路径 TextBox + 浏览文件夹按钮（FolderPickerOpenOptions）
5. 行为：自动保存间隔（禁用/1/5/10分钟）、清除确认 ToggleSwitch、默认缩放（75%/100%/125%/150%）
6. 新增页面切换动画选项：淡入淡出/水平滑动/垂直滑动/滑动+淡出/无动画
7. AppSettings 扩展：PageTransitionType 枚举和 TransitionAnimation 属性
8. MainShellViewModel 注入 IApplicationFacade，管理 IPageTransition，启动时加载动画设置
9. MainWindow 用 TransitioningContentControl 替换 ContentControl
10. SaveSettingsCommand 持久化所有设置；ResetDefaultsCommand 恢复默认值（含确认对话框）
11. 所有显示内容绑定到 ViewModel 属性，AXAML 零硬编码字符串

**Test Strategy:**

dotnet build + dotnet test (116/116) 全部通过
