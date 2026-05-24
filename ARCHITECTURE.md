一、项目目标与核心原则

目标：构建一个高度模块化、可扩展、易维护的.NET10跨平台桌面座位安排与轮换系统。

核心原则：

· 面向接口编程：模块间依赖抽象而非具体实现。
· 单一职责：每个模块仅负责明确的功能领域。
· 开闭原则：对扩展开放（新增布局/策略/数据源），对修改封闭。
· 依赖注入：通过 DI 容器管理组件生命周期与依赖。
· 配置驱动：行为由外部配置文件定义，减少硬编码。

---

二、整体架构分层

采用经典三层架构 + 插件化扩展：

```
┌─────────────────────────────────────────────────┐
│               Presentation Layer                 │
│        (Avalonia UI)                        │
└─────────────────────────────────────────────────┘
                        │
┌─────────────────────────────────────────────────┐
│            Application / Orchestration           │
│     - 编排数据加载、布局构建、策略执行流程          │
│     - IApplicationFacade (外观接口)              │
│     - 依赖注入容器 (Microsoft.Extensions.DI)      │
└─────────────────────────────────────────────────┘
                        │
┌─────────────────────────────────────────────────┐
│                  Domain Layer                    │
│  ┌──────────┐ ┌──────────┐ ┌──────────────────┐ │
│  │Entities  │ │Value Obj │ │Strategy Interface│ │
│  │(Student, │ │(Seat,    │ │(ISeatingStrategy)│ │
│  │Classroom)│ │Position) │ │                  │ │
│  └──────────┘ └──────────┘ └──────────────────┘ │
└─────────────────────────────────────────────────┘
                        │
┌─────────────────────────────────────────────────┐
│              Infrastructure Layer                │
│  ┌────────────────────┐  ┌─────────────────────┐│
│  │ IStudentProvider   │  │ ILayoutDefinition   ││
│  │ (Xlsx/Csv/Json实现)│  │ (Grid/Circle/Stadium)││
│  └────────────────────┘  └─────────────────────┘│
└─────────────────────────────────────────────────┘
```

项目结构规划：

```
A_Pair.slnx
├── A_Pair.Core              # 领域核心
├── A_Pair.Contracts         # 共享契约（插件接口）
├── A_Pair.Application       # 应用层（编排、策略调度）
├── A_Pair.Infrastructure    # 基础设施（数据访问、布局实现）
├── A_Pair.Plugins.Sdk       # 插件 SDK
├── A_Pair.Presentation.Avalonia  # Avalonia UI 主程序
└── Tests/                               # 单元测试与集成测试
```

---

三、核心领域模型

3.1 学生（Student）

```csharp
public class Student
{
    public string Id { get; set; }                     // 唯一标识
    public string Name { get; set; }
    public float? Height { get; set; }                 // 身高(cm)
    public Gender? Gender { get; set; }
    public bool NeedsFrontRow { get; set; }            // 是否需要前排
    
    // 轮换记录（环形缓冲区，存储最近N次座位ID）
    public CircularHistory<string> RecentSeatHistory { get; set; } = new(3);
    
    // 轮换权重（用于距离讲台轮换算法）
    public int FrontRowPreferenceScore { get; set; }
    
    // 扩展数据挂载点（插件使用）
    public AttributeBag Extensions { get; set; } = new();
}
```

3.2 座位（Seat）

```csharp
public abstract class Seat
{
    public string Id { get; set; }              // 逻辑ID
    public abstract SeatType Type { get; }
    public string LogicalGroup { get; set; }    // 逻辑分区
    public abstract object GeometryData { get; }
    
    public bool IsAvailable { get; set; } = true;
    public bool IsFixed { get; set; }
    public string OccupantId { get; set; }
    
    public AttributeBag Extensions { get; set; } = new();
}
```

具体实现：

· GridSeat：网格布局（行列）
· PolarSeat：极坐标布局（环形、扇形）

3.3 教室布局（ClassroomLayout）

支持网格、扇形、自由点等多种形式，并通过障碍物列表处理柱子、讲台等非常规结构。

```csharp
public class ClassroomLayoutDefinition
{
    public string LayoutType { get; set; }       // "Grid", "Polar", "Freeform"
    public LayoutMetadata Metadata { get; set; }
    public List<Seat> Seats { get; set; }
    public List<Obstacle> Obstacles { get; set; }
}
```

3.4 座位快照（SeatingSnapshot）

用于历史版本管理与回滚。

```csharp
public class SeatingSnapshot
{
    public string Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Description { get; set; }
    public string LayoutId { get; set; }
    public Dictionary<string, string> SeatAssignments { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}
```

---

四、策略引擎设计

4.1 策略接口

```csharp
public interface ISeatingStrategy
{
    string Id { get; }
    string Name { get; }
    int Priority { get; set; }      // 数值越小越先执行
    bool IsEnabled { get; set; }
    
    Task<StrategyExecutionResult> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken);
    ValidationResult ValidateConfiguration();
}
```

4.2 工作区（Workspace）

提供可修改的座位视图，隔离原始数据，记录修改历史。

```csharp
public class SeatingWorkspace
{
    public IReadOnlyList<Student> Students { get; }
    public SeatingContext Context { get; }
    
    public bool TryAssignSeat(string seatId, string studentId, out string error);
    public IEnumerable<Seat> GetEmptySeats();
    public IEnumerable<Seat> FindSeats(Func<Seat, bool> predicate);
}
```

4.3 执行管道

按优先级升序依次执行策略，后执行的策略可覆盖先前分配。

```csharp
public class StrategyExecutionPipeline
{
    public async Task<SeatingPlan> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken)
    {
        foreach (var strategy in _strategies.OrderBy(s => s.Priority).Where(s => s.IsEnabled))
        {
            var result = await strategy.ExecuteAsync(workspace, cancellationToken);
            // 记录修改与日志
        }
        return workspace.BuildSeatingPlan();
    }
}
```

4.4 内置策略示例

策略 优先级 职责
RandomFillStrategy 10 最不优先，将剩余学生随机填入空位
FrontRowRotationStrategy 30 基于累计分数轮换前排座位
DeskMateStrategy 50 安排同桌组彼此靠近
FixedSeatStrategy 100 最优先（最后执行），强制应用固定座位

4.5 插件化策略

· Assembly 插件：编译为 DLL，实现 IPluginSeatingStrategy。
· Script 插件：支持 Lua / C# Script，逻辑完全由脚本文件描述。
· 插件清单：plugin.manifest.json 定义 ID、类型、入口文件、优先级等。

---

五、配置文件与存储结构

5.1 配置文件分类

类别 文件位置 作用域
程序设置 AppSettings.json 全局
会场定义 Venues/*.venue.json 单个物理空间
人员名单 Rosters/*.roster.json 单次活动
座位安排结果 Assignments/{venue}/{date}/snapshot.json 特定场次
插件配置 Plugins/{plugin-id}/config.json 单个插件

5.2 文件夹布局

```
A_Pair/
├── AppSettings.json
├── Venues/
├── Rosters/
├── Assignments/
├── Plugins/
├── Backups/
├── Logs/
└── Temp/
```

5.3 版本管理与升级

· 配置文件包含 `version` 字段（`VenueFile` v1.1、`RosterFile` v1.0、`SeatingSnapshot` v1.0、`AppSettings` v1.0、`StrategyConfig` v1.0、`VenueSnapshotInfo` v1.0）
· 各文件类型当前版本号记录于 `A_Pair.Infrastructure/Migration/file_versions.json`（嵌入资源，随程序编译）
· 加载时 `FileMigrationService` 读取文件版本号，链式执行注册的 `IFileMigrator` 实现完成向前迁移（不支持回退）
· 迁移器按文件类型组织：`Migration/Migrators/{FileType}Migrators.cs`，每个版本步进为一个嵌套类（如 `VenueMigrators.Step_1_0_to_1_1`），DI 中以 `IFileMigrator` 注册
· 反序列化前通过 `JsonNode` 操作完成迁移，避免目标类型 Schema 不一致
· JSON 序列化约定：camelCase 命名字段；`layoutType` 为数字枚举、`layoutTypeString` 为字符串；Seat 多态通过 `SeatJsonConverter` 的 `Type` 鉴别器
· 座位快照支持父子关系，便于追溯与回滚

5.4 敏感数据保护

· 字段级 AES-256 加密（标记 [SensitiveData] 的属性）。
· 文件级加密（用户密码保护整个 Roster 文件）。
· 密钥使用平台安全存储（DPAPI / libsecret）。

---

六、跨平台表示层设计

6.1 UI 框架选择

Avalonia UI，支持 Windows / macOS / Linux，原生 MVVM 支持。

6.2 表示层与应用层通信

采用 外观模式 + 依赖注入：

```csharp
public interface IApplicationFacade
{
    Task<AppConfiguration> LoadConfigurationAsync(string path);
    Task<List<Student>> LoadStudentsAsync(string source);
    Task<SeatingPlan> GenerateSeatingAsync(SeatingRequest request, IProgress<SeatingProgress> progress = null);
    Task ExportSeatingPlanAsync(SeatingPlan plan, ExportOptions options);
    Task<IReadOnlyList<SeatingSnapshot>> GetSnapshotsAsync(string venueId);
    Task RollbackToSnapshotAsync(string snapshotId);
}
```

ViewModel 通过构造函数注入 IApplicationFacade，调用业务逻辑。

6.3 进度报告与异步

· 使用 IProgress<SeatingProgress> 报告长时间操作状态。
· 命令模式（ICommand）绑定 UI 操作。

---

七、补充考量与边缘情况

7.1 非功能性需求

类别 处理方案
性能 高效数据结构、并行执行无冲突策略、进度刷新限频
并发 文件锁机制，可选 SQLite 支持多人编辑
日志 Serilog 分级输出，审计关键操作
国际化 .resx 资源文件 + IStringLocalizer
内存管理 快照文件化，内存仅保留索引，实现 IDisposable

7.2 业务流程闭环

· 手动微调：支持拖拽换位，记录手动覆盖标记。
· 撤销/重做：命令模式实现操作栈。
· 数据验证：导入后调用 IDataValidator 返回错误列表。
· 打印/PDF 导出：集成 QuestPDF 或 PdfSharp。

7.3 异常处理

场景 策略
座位不足 提前容量检查，抛出明确异常
固定座位冲突 配置验证阶段检测，后者覆盖并警告
插件加载失败 提供“安全模式”跳过插件
配置文件损坏 自动加载最近有效备份
策略执行超时 CancellationToken + 超时设置

7.4 部署与更新

· 打包格式：Windows (.msi / .zip)、macOS (.app / .pkg)、Linux (AppImage / Flatpak)。
· 自动更新：可选集成 Velopack 或 Squirrel。

7.5 测试策略

· 单元测试：核心算法。
· 集成测试：完整管道。
· 快照测试：验证输出一致性。
· 性能基准：BenchmarkDotNet。
· 沙箱测试：脚本死循环隔离。

7.6 安全与隐私

· 脚本沙箱：禁用 IO/网络，限制执行时间与内存。
· 导出匿名化：支持仅显示学号。
· 日志脱敏：避免打印完整姓名。

---

八、后续开发里程碑

阶段 内容 产出
Phase 1 领域建模、基础架构搭建 核心实体、DI 配置、网格布局
Phase 2 数据加载与导出 Xlsx/Csv 读取、Excel 导出
Phase 3 内置策略实现 RandomFill、FrontRowRotation、FixedSeat
Phase 4 插件系统 插件管理器、Assembly 加载
Phase 5 脚本支持 Lua 引擎集成、受限 API
Phase 6 高级布局 圆形、扇形阶梯教室
Phase 7 CLI 工具完善 命令行参数、交互向导
Phase 8 测试与文档 单元测试覆盖、用户手册

---

九、附录：关键接口速查

接口 所在层 用途
IApplicationFacade Application UI 与业务逻辑通信外观
ISeatingStrategy Domain 座位安排策略
IPluginSeatingStrategy Contracts 插件策略契约
IClassroomLayout Domain 教室布局抽象
IStudentProvider Infrastructure 学生数据加载
ISeatingPlanExporter Infrastructure 结果导出
IConflictResolver Application 座位冲突解决
IPluginManager Application 插件发现与加载

---

文档版本：1.2
最后更新：2026-05-24
适用项目：座位安排/轮换系统（跨平台桌面版 .NET 10 + Avalonia 12）
