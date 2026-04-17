# A_Pair.Core

## 项目简介
领域核心层，定义学生、座位、布局等基础实体与领域服务。

## 主要类型与接口

### Student
- 属性：Id, Name, Height, Gender, NeedsFrontRow, RecentSeatHistory, FrontRowPreferenceScore, Extensions
- 用途：表示学生基本信息及排座相关属性。

### Seat（抽象类）
- 属性：Id, Type, LogicalGroup, GeometryData, IsAvailable, IsFixed, OccupantId, Extensions
- 用途：表示教室中的一个座位，支持多种布局。

### CircularHistory<T>
- 方法：
  - Add(T item)：添加历史记录
  - Contains(T item)：判断是否存在
  - GetAll()：获取所有历史记录
- 用途：用于学生轮换历史等环形缓冲区场景。

### AttributeBag
- 用途：扩展属性容器，支持插件或自定义数据。

### 领域策略接口
- ISeatingStrategy：座位分配策略接口

## 对外接口

- 所有实体类均为不可变对象，外部仅可通过构造函数或专用方法创建。
- 提供领域服务接口（如 ISeatingStrategy）供应用层调用。

## 示例
```csharp
var student = new Student { Id = "001", Name = "张三" };
student.RecentSeatHistory.Add("A1");
```