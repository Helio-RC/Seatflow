# Task ID: 4

**Title:** 会场配置视图（VenueConfigurationView）

**Status:** pending

**Dependencies:** 2 ✓

**Priority:** high

**Description:** 实现会场布局的创建、编辑和可视化预览界面

**Details:**

1. 创建 VenueConfigurationView.axaml
2. 会场列表管理（新建/删除/重命名）
3. 布局类型选择（Grid/Polar/Freeform）
4. 动态参数面板
5. 布局预览画布
6. 调用 SaveVenueAsync/LoadVenueAsync 持久化

**Test Strategy:**

手动测试：创建 Grid 布局 → 调整参数 → 预览更新 → 保存 → 重新加载
