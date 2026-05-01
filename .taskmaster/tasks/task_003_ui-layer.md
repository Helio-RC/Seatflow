# Task ID: 3

**Title:** 数据管理视图（DataManagementView）

**Status:** done

**Dependencies:** 2 ✓

**Priority:** high

**Description:** 实现学生数据导入、预览表格和导出功能的完整界面

**Details:**

1. 创建 DataManagementView.axaml
2. DataManagementViewModel：调用 IApplicationFacade.LoadStudentsAsync 导入、DataGrid 预览、ExportStudentsAsync 导出
3. 集成 Avalonia 文件对话框
4. 导入错误列表展示
5. 进度条显示导入/导出进度

**Implementation notes (2026-05-02):**

- 创建 `Services/IFileService.cs` 和 `Services/FileService.cs` — 文件对话框服务抽象
- 创建 `Infrastructure/Providers/CompositeStudentProvider.cs` — 根据文件扩展名分发的复合 IStudentProvider
- `ServiceCollectionExtensions.cs` — 注册 IStudentProvider (TryAddSingleton)
- `Program.cs` — 注册 IFileService
- `App.axaml.cs` — 在窗口创建后设置 TopLevel 引用
- `DataManagementViewModel.cs` — 完整实现：Import/Export(CSV/Excel/JSON)/ClearData 命令
- `DataManagementView.axaml` — 工具栏 + DataGrid + 空状态提示 + 加载遮罩 + 状态栏
- 添加 `Avalonia.Controls.DataGrid` 12.0.0 NuGet 包
- dotnet build 通过，全部 101 个测试通过

**Test Strategy:**

手动测试：选择 CSV 导入 → 预览 → 导出 XLSX → 验证
