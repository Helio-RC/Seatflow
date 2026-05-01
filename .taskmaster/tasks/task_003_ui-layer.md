# Task ID: 3

**Title:** 数据管理视图（DataManagementView）

**Status:** pending

**Dependencies:** 2 ✓

**Priority:** high

**Description:** 实现学生数据导入、预览表格和导出功能的完整界面

**Details:**

1. 创建 DataManagementView.axaml
2. DataManagementViewModel：调用 IApplicationFacade.LoadStudentsAsync 导入、DataGrid 预览、ExportStudentsAsync 导出
3. 集成 Avalonia 文件对话框
4. 导入错误列表展示
5. 进度条显示导入/导出进度

**Test Strategy:**

手动测试：选择 CSV 导入 → 预览 → 导出 XLSX → 验证
