# Task ID: 3

**Title:** 数据管理视图（DataManagementView）

**Status:** done

**Dependencies:** 2 ✓

**Priority:** high

**Description:** 实现学生数据导入、预览表格和导出功能的完整界面

**Details:**

1. 创建 DataManagementView.axaml
2. DataManagementViewModel：调用 IApplicationFacade.LoadStudentsAsync 导入、ListBox 预览、ExportStudentsAsync 导出
3. 集成 Avalonia 文件对话框
4. 导入错误列表展示
5. 进度条显示导入/导出进度

**Implementation notes (2026-05-02):**

基础实现：
- `Services/IFileService.cs` + `FileService.cs` — 文件对话框服务
- `Services/IDialogService.cs` + `DialogService.cs` — 错误/警告/确认弹窗
- `Views/DialogWindow.axaml` + `.cs` — 模态弹窗
- `Infrastructure/Providers/CompositeStudentProvider.cs` — 扩展名分发
- `Infrastructure/Providers/StudentDataMapping.cs` — 中英文列名、值转换、注释行跳过
- `DataManagementViewModel.cs` — Import/Export/模板/清除 + 全面错误处理
- `DataManagementView.axaml` — 工具栏 + ListBox 表格 + 空状态 + 加载遮罩 + 状态栏
- ViewModels 改为 Singleton 注册，页面切换不丢失数据
- CSV/UFT-8 BOM 修复，Excel 中文不乱码
- DataGrid → ListBox + DataTemplate（编译绑定兼容）

自动保存功能：
- `Core/Models/StudentDatasetInfo.cs` — 数据集 DTO
- `Core/Providers/IStudentDatasetRepository.cs` — 仓储接口
- `Infrastructure/Providers/JsonStudentDatasetRepository.cs` — JSON 文件仓储，存储在 AppData/Rosters/
- `IApplicationFacade` 新增 4 个方法：Save/Load/List/DeleteStudentDataset
- 导入成功后自动保存到 AppData/Rosters/
- 左侧数据集面板：ListBox 展示已保存数据集 + 加载/删除按钮

**Test Strategy:**
- dotnet test 全部通过（104 tests）
- 手动：导入 CSV → 左侧自动出现数据集 → 切换页面 → 数据仍在 → 点击加载 → 表格显示
