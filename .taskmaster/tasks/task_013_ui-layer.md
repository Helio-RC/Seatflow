# Task ID: 13

**Title:** 设置视图（SettingsView）

**Status:** pending

**Dependencies:** 2 ✓

**Priority:** low

**Description:** 实现应用程序设置页面，支持主题切换、语言、数据目录等配置项

**Details:**

1. 创建 SettingsView.axaml
2. 主题切换（亮色/暗色/跟随系统）
3. 语言选择
4. 数据目录路径配置
5. 自动保存间隔设置
6. 重置为默认值
7. 通过 IApplicationFacade 读写 AppSettings

**Test Strategy:**

手动测试：切换主题 → 修改设置 → 重启 → 验证持久化
