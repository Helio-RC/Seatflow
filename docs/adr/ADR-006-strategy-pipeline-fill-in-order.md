# ADR-006: 策略管道采用"按优先级填空"模型

## 状态
已接受

## 日期
2026-05-31

## 背景

管道最初设计为 **"后可覆盖"（override）** 模型：低 Priority 策略先执行建立基线，高 Priority 策略后执行可推翻前序分配。架构文档和接口注释均以此模型描述。

但在实现验证中发现，该模型在当前 Workspace API 下不可行：

1. `GetEmptySeats()` 仅返回 `IsAvailable == true && !IsFixed` 的座位
2. `TryAssignSeat()` 拒绝 `IsAvailable == false` 的座位
3. 只有 `FixedSeatStrategy` 通过直接操控 `OccupantId=null; IsAvailable=true` 绕过了门禁
4. `FrontRowRotationStrategy` 和 `DeskMateStrategy` 使用标准的 `GetEmptySeats+TryAssignSeat` 路径，无法覆盖已占座位

实际管道行为：
```
RandomFill(10)     → GetEmptySeats=全空 → 填满所有座位
FrontRowRotation(30) → GetEmptySeats=0 → 跳过（死代码）
DeskMate(50)       → GetEmptySeats=0 → 跳过（死代码）
FixedSeat(100)     → 直接操控 OccupantId → 覆盖成功
```

最终排座结果 = RandomFill 随机分布 + FixedSeat 硬覆盖。前排轮换和同桌分组从未实际生效。

## 决策

采用 **"按优先级填空"（Fill-in-Order）** 模型：

- 策略按 Priority 升序执行（数值越小越先执行）
- 先执行的策略从空座中优先挑选，后执行的在剩余空座中择优
- **不存在覆盖**——先占的座位不会被推翻
- IsFixed 标志是唯一的保护机制（FixedSeat 最先执行，标记座位为 IsFixed=true，后续策略的 GetEmptySeats 自动排除这些座位）

### 新 Priority

| 策略 | Priority | 执行顺序 | 职责 |
|------|----------|----------|------|
| FixedSeatStrategy | 10 | 第1 | 锁定固定座位，IsFixed=true 自动保护 |
| FrontRowRotationStrategy | 20 | 第2 | 在非固定空座中填前排 |
| DeskMateStrategy | 30 | 第3 | 在剩余空座中拼连续块 |
| RandomFillStrategy | 100 | 最后 | 填满所有剩余空座 |

### 冲突解决

策略间的座位冲突 = Priority 数值决定（先到先得）。如果 FrontRowRotation(20) 使用了 DeskMate(30) 想要的相邻座位，DeskMate 只能在其他位置寻找连续块。这是有意设计——用户可通过调整策略优先级控制资源分配顺序。

## 考虑的替代方案

### 方案 B：给 Workspace 加 ForceAssignSeat + 孤儿追踪
- 优点：保留"后可覆盖"语义
- 缺点：FrontRowRotation 和 DeskMate 需要完全重写分配逻辑（~100+ 行改动）；需要孤儿学生追踪机制 + RandomFill 收容；管道复杂度大幅上升
- 拒绝：对 1.0-preview 风险过高，且 DeskMate 和 FrontRowRotation 在"后被覆盖"场景下的前置条件（完整连续块、空前排座）更难满足

### 方案 C：保留原 Priority 不变，只修复注释
- 优点：零代码改动
- 缺点：FrontRowRotation 和 DeskMate 仍然是死代码，管道的核心价值（前排轮换+同桌分组）无法交付
- 拒绝：不可接受的 1.0 质量问题

## 后果

### 正面
- 前排轮换和同桌分组策略现在在管道中**实际生效**
- 改动量小（仅 Priority 常量 + 文档），风险低
- 逻辑简单明了："先到先得"，易于理解和验证
- 策略间冲突由 Priority 数值显式决定，可调试

### 负面
- 失去了"后可覆盖"的灵活性——用户无法配置"即使前排已有人，也要替换为近视学生"
- DeskMate 使用前排的连续块后，FrontRowRotation 就没有前排座可用——反之亦然
- 未来如果需要真正的"覆盖"语义，需要重新设计 Workspace API（加入 ForceAssign、Swap 等操作）

### 未来方向
- 加入管道的端到端集成测试，验证 Fill-in-Order 顺序下的实际行为
- UI 层暴露 Priority 拖拽排序，让用户显式控制策略间的资源分配优先级
- 如果用户反馈需要"覆盖"行为，在 Workspace 中引入 `ForceAssignSeat` 或 `SwapSeats` 方法，同时追踪被替换学生
