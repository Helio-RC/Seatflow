# 策略数据持久化与容错能力分析报告

> 分析日期：2026-06-19  
> 分析范围：全部 6 个内置策略的数据依赖链、持久化机制、以及在会场/人员数据变更后的容错能力

---

## 1. 数据持久化架构总览

A_Pair 的策略数据分为三层存储，各有不同的生命周期：

```
┌─────────────────────────────────────────────────────────────────┐
│  Type A: StrategyConfig（策略全局配置）                           │
│  路径: {AppData}/StrategyConfig/{strategyId}.config.json         │
│  内容: Priority, IsEnabled, Parameters (如 HistoryWindowSize)    │
│  影响: ALL strategies                                            │
│  加载时机: GetStrategiesAsync() 仅用于展示；❌ 启动时不注入实例     │
├─────────────────────────────────────────────────────────────────┤
│  Type B: StrategyDatasetConfig（按数据集+会场的代码块配置）        │
│  路径: {AppData}/StrategyConfig/{strategyId}/{dsHalf}-{vHalf}.json│
│  内容: 配置行 (StudentId, SeatRow/Column, Values[...])           │
│  影响: FixedSeat, DeskMate, GenderRestrictedSeat                │
│  加载时机: GenerateSeatingAsync → ApplyCodeBlockConfigsAsync     │
├─────────────────────────────────────────────────────────────────┤
│  Type C: 历史快照（自包含）                                       │
│  路径: {basePath}/{venueId}/yyyyMMdd/*.json                      │
│  内容: SeatAssignments + 嵌入会场布局 (venueFile/venueLayout)      │
│  影响: FrontRowRotation, NoRepeatDeskMate                        │
│  加载时机: GenerateSeatingAsync → HistoryLoaders                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. 逐策略分析

### 2.1 FixedSeatStrategy（固定座位）

**类型**：独立策略 | **优先级**：100 | **默认启用**：是

**数据依赖链**：
```
StrategyDatasetConfig.Rows[]
  ├── SeatRow + SeatColumn  ──→  FindSeatByPosition(layout, row)  ──→  Seat.Id
  └── StudentId             ──→  FixedAssignments[seatId] = studentId
                                      ↓
                               ExecuteAsync: workspace.TryAssignSeat(seat.Id, studentId)
```

**文件匹配**：`{datasetId前半}-{venueId前半}.config.json`，同时绑定 datasetId 和 venueId。ID 本身是稳定的（编辑/重命名均不变），但**重新导入数据集**会生成新 ID——这是教师更新名单的常见操作。

| 变更场景 | 容错结果 | 严重度 | 说明 |
|----------|:--------:|:------:|------|
| 会场编辑（位置仍存在） | ✅ | — | `FindSeatByPosition` 按 (Row, Column) 匹配成功 |
| 会场缩小（位置消失） | ✅ 已修复 | ~~中~~ | `CleanInvalidSeatRows` 自动删除越界行并保存 |
| 会场文件删除 | ❌ | **高** | `venueLayout` 为 null → 全部行跳过 → 配置完全失效 |
| 学生被移除（编辑名单） | ✅ 已修复 | ~~高~~ | `CleanFixedSeatDeletedStudents` 自动删除幽灵学生行并保存 |
| 重新导入数据集（新 ID） | ❌ | **高** | 旧文件中的 datasetId 和所有学生 ID 全部失配 → 配置不加载 |

**关键代码** (`ApplicationFacade.cs:897-911`)：
```csharp
var seat = FindSeatByPosition(venueLayout, row);
if (seat is not null)
    assignments[seat.Id] = row.StudentId;
// 找不到座位 → 静默跳过，不产生任何消息
```

**哈希检测**：`StrategyDatasetConfig` 中有 `StudentsHash` 和 `ContentHash`，通过 `CheckDatasetIntegrityAsync()` 可检测变更。但此方法**从未被 UI 调用**，属于已实现但未集成的死功能。

---

### 2.2 DeskMateStrategy（同桌分组）

**类型**：依赖策略 | **优先级**：50 | **默认启用**：否

**数据依赖链**：
```
StrategyDatasetConfig.Rows[]
  ├── StudentId              ──→  group.StudentIds.Add()
  └── Values["student1"/...] ──→  group.StudentIds.Add()
                                      ↓
  ApplyDeskMateConfig: Groups.Add(group)  (if group.StudentIds.Count >= 2)
                                      ↓
  EvaluateAsync: FindGroupForStudent(student) → 协调分配
```

**文件匹配**：与 FixedSeat 相同，按 `(datasetId, venueId)` 绑定。

| 变更场景 | 容错结果 | 严重度 | 说明 |
|----------|:--------:|:------:|------|
| 会场任何修改 | ✅ | — | **不引用座位位置**，完全无影响 |
| 会场文件删除 | ✅ | — | 同上 |
| **SeatsPerDesk 变更** | ✅ | — | 运行时从当前会场 `GridLayoutMetadata.SeatsPerDesk` 动态读取 |
| 组内部分学生被移除（编辑名单） | ✅ 已修复 | ~~中~~ | `CleanDeskMateDeletedStudents` 自动移除失效学生，剩余不足 2 人则删行，满 2 人则降级组 |
| 重新导入数据集（新 ID） | ❌ | **高** | 旧文件中的 datasetId 和所有学生 ID 全部失配 → 组破碎 |
| 新增学生 | ✅ | — | 不影响已有组 |

**容错亮点**：DeskMate 是唯一的座位位置无关型代码块策略，对会场修改天然免疫。

**关键代码** (`DeskMateStrategy.cs:357-371`)：
```csharp
// 运行时过滤：只排除"已分配"的学生，不校验"是否存在"
var activeIds = group.StudentIds
    .Where(id => id == student.Id || !assignedIds.Contains(id))
    .ToList();
```

---

### 2.3 GenderRestrictedSeatStrategy（性别限制座位）

**类型**：依赖策略 | **优先级**：45 | **默认启用**：否

**数据依赖链**：
```
StrategyDatasetConfig.Rows[]
  ├── SeatRow + SeatColumn  ──→  FindSeatByPosition(layout, row)  ──→  Seat.Id
  └── Values["Gender"]      ──→  Gender.Male / Female
                                      ↓
  SetRestrictions: SeatGenderRestrictions[seatId] = gender
                                      ↓
  EvaluateAsync: 检查 targetSeat 性别限制 → 匹配/重定向/Reject
```

| 变更场景 | 容错结果 | 严重度 | 说明 |
|----------|:--------:|:------:|------|
| 会场编辑（位置仍存在） | ✅ | — | `FindSeatByPosition` 按位置匹配 |
| 会场缩小（位置消失） | ✅ 已修复 | ~~中~~ | `CleanInvalidSeatRows` 自动删除越界行并保存 |
| 会场文件删除 | ❌ | **高** | 无 layout → 全部行跳过 |
| 学生任何变动 | ✅ | — | **完全不依赖学生 ID**，限制绑定在座位上 |
| 重新导入数据集（新 ID） | ❌ | **高** | 旧文件的 datasetId 失配 → 配置不加载 |

**容错特点**：对人员变动完全免疫（唯一做到这点的代码块策略），但对会场变动敏感（与 FixedSeat 共享 `FindSeatByPosition` 机制）。

**重定向优化**：当学生性别不匹配目标座位时，会优先搜索匹配性别的受限空座位直接分配（`Handled`），不消耗重掷次数。重掷耗尽后才强制分配。

---

### 2.4 FrontRowRotationStrategy（前排轮换）

**类型**：独立策略 | **优先级**：50 | **默认启用**：否

**数据依赖链（双层）**：

**层 A — StrategyConfig（全局参数）**：
```
StrategyConfig.Parameters["HistoryWeight"]      ──→  Config.HistoryWeight (默认 10)
StrategyConfig.Parameters["NeedsFrontRowBonus"] ──→  Config.NeedsFrontRowBonus (默认 1000)
StrategyConfig.Parameters["FrontRowCount"]      ──→  Config.FrontRowCount (默认 1)
```
⚠️ **致命问题**：`StrategyConfig` 仅在 `SaveStrategyConfigAsync` 时注入 DI 单例。应用重启后 DI 单例恢复默认值，且无启动初始化代码加载持久化配置。用户若重启后直接进入排座页生成，将使用默认参数而非上次保存的值。

**层 B — 历史快照（前排记录）**：
```
快照 {venueId}/yyyyMMdd/*.json
  → FrontRowHistoryLoader.PopulateFrontRowHistoryAsync()
  → 读取嵌入布局 → IdentifyFrontRowSeats() → 提取前排座位 ID
  → Student.RecentSeatHistory.Add(seatId)
  → 执行时: score -= frontRowHistoryCount × HistoryWeight
```

| 变更场景 | 容错结果 | 严重度 | 说明 |
|----------|:--------:|:------:|------|
| 会场编辑/删除 | ✅ | — | 快照自包含嵌入布局 |
| 学生被移除（编辑名单） | ⚠️ | **低** | `studentMap.TryGetValue` → 仅跳过该学生；**其他学生历史不受影响** |
| 重新导入数据集（新 ID） | ❌ | **高** | 快照中存旧学生 ID → 无法匹配 |
| 参数未持久化（重启） | ✅ 已修复 | ~~高~~ | `RestorePersistedStrategyConfigsAsync` 在 `GenerateSeatingAsync` 中从磁盘恢复 |

**与 NoRepeatDeskMate 的关键差异**：

```csharp
// FrontRowHistoryLoader: 单学生级别过滤（好）
if (!studentMap.TryGetValue(studentId, out var student)) continue;
student.RecentSeatHistory.Add(seatId);  // 仅跳过该学生

// NoRepeatDeskMateHistoryLoader: 学生对级别过滤（差）
if (!validStudentIds.Contains(occA) || !validStudentIds.Contains(occB)) continue;
// ^ 任意一方不存在 → 整对丢弃，剩余学生也失去历史
```

---

### 2.5 NoRepeatDeskMateStrategy（非重复同桌）

**类型**：依赖策略 | **优先级**：40 | **默认启用**：否

**数据依赖链（双层）**：

**层 A — StrategyConfig（全局参数）**：
```
StrategyConfig.Parameters["HistoryWindowSize"] ──→ Config.HistoryWindowSize (默认 10)
```
与 FrontRowRotation 相同的重启丢参数问题。

**层 B — 历史快照（同桌对）**：
```
快照 {venueId}/yyyyMMdd/*.json
  → NoRepeatDeskMateHistoryLoader.PopulateDeskMateHistoryAsync()
  → ExtractDeskMatePairsFromSnapshot():
      1. 读取嵌入布局 → 解析 AreDeskMates 判定相邻座位
      2. 过滤：双方学生必须都在 validStudentIds 中
      3. 规范化 (smallerId, largerId) → 存入 HashSet
  → SetPastDeskMatePairs()
  → EvaluateAsync: 检查相邻已占座位中是否有历史同桌
```

| 变更场景 | 容错结果 | 严重度 | 说明 |
|----------|:--------:|:------:|------|
| 会场编辑/删除 | ✅ | — | 快照自包含嵌入布局 |
| SeatsPerDesk 变更 | ✅ | — | 历史提取用旧布局定义，当前评估用新布局定义，语义正确 |
| 学生被移除（编辑名单） | ✅ 已修复 | ~~中~~ | 改为单学生级过滤，保留留存学生的历史记录；另一方 ID 即便不存在也不影响 |
| 重新导入数据集（新 ID） | ❌ | **高** | 快照中旧学生 ID 无法匹配 |
| 参数未持久化（重启） | ✅ 已修复 | ~~高~~ | `RestorePersistedStrategyConfigsAsync` 从磁盘恢复 |
| 无历史数据 | ✅ | — | `_pastDeskMatePairs.Count == 0` → 直接 Approve |
| 重掷耗尽 | ✅ | — | 强制分配 + LogWarning，不阻断管道 |

---

### 2.6 DefragStrategy（碎片整理）

**类型**：独立策略 | **优先级**：0 | **默认启用**：否

**无外部数据依赖**。行为完全由位置驱动（扫描空座、前移无约束学生）。容错无问题。

唯一的运行时数据来自管道内注入：
- `SetConstrainedStudentIds(固定座位学生 + DeskMate 组学生)` — 这些在策略执行时动态计算

---

### 2.7 RandomFillStrategy（随机填充）

**类型**：独立+宿主策略 | **优先级**：1 | **默认启用**：是

**无外部数据依赖**。作为依赖策略的宿主，它承载 DeskMate、GenderRestrictedSeat、NoRepeatDeskMate 在其分配循环中运行。自身无持久化需求。

---

## 3. 交叉问题

### 3.1 ✅ 已修复：StrategyConfig 重启后丢失

**修复方式**：在 `GenerateSeatingAsync` 步骤 4c 中新增 `RestorePersistedStrategyConfigsAsync`，从 `StrategyConfigFileRepository.LoadAllAsync()` 加载所有持久化配置并应用到 DI 单例。独立的 `ApplyPersistedConfigToInstance` 方法被 `SaveStrategyConfigAsync` 和恢复逻辑共用。

### 3.2 🟡 文件名双重绑定 → 重新导入数据集导致配置失联

FixedSeat、DeskMate、GenderRestrictedSeat 的配置文件按 `{datasetId前半}-{venueId前半}.config.json` 命名。会场和数据集 ID 在编辑/重命名时保持不变，但**重新导入数据集（CSV/XLSX）会生成全新的 ID**——这是教师每学期更新名单的常规操作，而旧配置文件无法跟随到新数据集。

`StrategyDatasetConfig` 内部也存储了 `DatasetId` 和 `VenueId` 字段用于匹配，但文件名本身已经做了第一层过滤——新数据集的 ID 不同，旧文件根本不会被枚举到。配置实际上丢失了，但磁盘文件还在。

### 3.3 🟡 哈希完整性检测已实现但未使用（未修改）

`CheckDatasetIntegrityAsync()` 在 API 层已完整实现，对比 `StudentsHash` 和 `ContentHash`。`StrategyDatasetConfig` 在保存时自动写入哈希。但**没有任何 UI 代码调用此方法**——数据变更的检测完全缺失，用户无法知道配置是否已失效。

### 3.4 ✅ 已修复：FindSeatByPosition 静默跳过

`CleanInvalidSeatRows` 在加载配置时自动检测并删除越界行，同时将清理后的配置持久化。用户下次打开策略配置页时看到的是已清理的有效数据。

### 3.5 ✅ 已修复：NoRepeatDeskMate 的连带数据丢失

删除了 `ExtractDeskMatePairsFromSnapshot` 中的 `validStudentIds` 过滤逻辑。历史同桌对不再因另一方不在当前名单中而被丢弃——留存学生保留完整历史记录，不存在的学生 ID 在 `EvaluateAsync` 中自然无匹配，不会造成负面影响。

---

## 4. 容错能力总矩阵

| 策略 | 会场编辑 | 会场删除 | SeatsPerDesk变更 | 学生编辑(移除) | 重新导入数据集 | 重启恢复 |
|------|:--:|:--:|:--:|:--:|:--:|:--:|
| **FixedSeat** | ✅ 已修复 | ❌ | — | ✅ 已修复 | ❌ | ✅ 已修复 |
| **DeskMate** | ✅ | ✅ | ✅ | ✅ 已修复 | ❌ | ✅ 已修复 |
| **GenderRestrictedSeat** | ✅ 已修复 | ❌ | — | ✅ | ❌ | ✅ 已修复 |
| **FrontRowRotation** | ✅ | ✅ | — | ⚠️ 单学生丢历史 | ❌ | ✅ 已修复 |
| **NoRepeatDeskMate** | ✅ | ✅ | ✅ | ✅ 已修复 | ❌ | ✅ 已修复 |
| **Defrag** | — | — | — | — | — | ✅ |
| **RandomFill** | — | — | — | — | — | ✅ |

图例：✅ 无影响　⚠️ 部分失效　❌ 完全失效　— 不适用

---

## 5. 各策略数据依赖类型速查

| 策略 | 依赖座位位置 | 依赖学生ID | 依赖会场ID | 依赖数据集ID | 依赖历史快照 |
|------|:--:|:--:|:--:|:--:|:--:|
| FixedSeat | ✅ Row+Col | ✅ | ✅ (文件名+字段) | ✅ (文件名+字段) | — |
| DeskMate | — | ✅ | ✅ (文件名) | ✅ (文件名) | — |
| GenderRestrictedSeat | ✅ Row+Col | — | ✅ (文件名+字段) | ✅ (文件名) | — |
| FrontRowRotation | — | ✅ | ✅ (快照查询) | — | ✅ |
| NoRepeatDeskMate | — | ✅ | ✅ (快照查询) | — | ✅ |
| Defrag | — | — | — | — | — |
| RandomFill | — | — | — | — | — |

> **关于 ID 依赖**：会场和数据集 ID 在编辑/重命名时保持不变，不会丢失。风险在于**重新导入数据集**（产生新 ID）或**新建会场替代旧会场**（刻意替换）。快照按 venueId 查询是正确行为——不同教室的历史不应混淆。

---

## 6. 改进建议

### P0 — 已完成 ✅

1. **StrategyConfig 重启恢复**：`RestorePersistedStrategyConfigsAsync` 在每次 `GenerateSeatingAsync` 时从磁盘加载配置并注入 DI 单例。

### P1 — 部分完成

2. **重新导入数据集后的配置迁移提示**：暂未实现，仍需设计。

3. **FindSeatByPosition 失败时自动清理**：`CleanInvalidSeatRows` 自动删除越界行并保存，不再静默跳过。

### P2 — 已完成 ✅

4. **NoRepeatDeskMate 改为单学生级过滤**：删除了 `ExtractDeskMatePairsFromSnapshot` 中的有效学生 ID 过滤逻辑。

---

## 7. 修复记录

| 日期 | 修复项 | 涉及方法 | 影响策略 |
|------|--------|----------|----------|
| 2026-06-19 | R3b 单学生级过滤 | `ExtractDeskMatePairsFromSnapshot`（删除过滤） | NoRepeatDeskMate |
| 2026-06-19 | R1 座位越界清理 | `CleanInvalidSeatRows` | FixedSeat, GenderRestrictedSeat |
| 2026-06-19 | R3a 缺失学生清理 | `CleanFixedSeatDeletedStudents`, `CleanDeskMateDeletedStudents` | FixedSeat, DeskMate |
| 2026-06-19 | R1+R3a 保存集成 | `SaveDatasetConfigAsync`，修改 `ApplyCodeBlockConfigsAsync` | FixedSeat, DeskMate, GenderRestrictedSeat |
| 2026-06-19 | R5 重启恢复 StrategyConfig | `ApplyPersistedConfigToInstance`, `RestorePersistedStrategyConfigsAsync` | 所有策略 |
| 2026-06-19 | R2 孤立配置清理 | `CleanupOrphanedDatasetConfigsAsync` | 所有代码块策略 |

### 新增文件

无。所有改动集中在已有文件中。

### 改动量

- `NoRepeatDeskMateHistoryLoader.cs`：删除 6 行（validStudentIds 过滤逻辑）
- `ApplicationFacade.cs`：新增 6 个方法（~160 行），修改 4 个方法（~25 行），插入 3 个调用点（~8 行）
