# A_Pair.Infrastructure

## 项目简介
基础设施层，负责数据访问、布局实现、导出等。

## 主要组件

### 学生数据提供器
- CsvStudentProvider：CSV 导入
- XlsxStudentProvider：Excel 导入
- JsonStudentProvider：JSON 导入

### 布局构建器
- GridLayoutBuilder：网格布局
- PolarLayoutBuilder：极坐标布局
- FreeformLayoutBuilder：自由点布局

### 导出器
- ExcelSeatingExporter：导出 Excel

### 仓储
- SeatingSnapshotRepository：快照存储

## 对外接口
- 通过接口（如 IStudentProvider）供应用层调用。

## 示例
```csharp
var provider = new CsvStudentProvider();
var students = provider.Load(path);
```