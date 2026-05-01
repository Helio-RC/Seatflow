# Task ID: 6

**Title:** 座位安排视图 — 座位图渲染（SeatingArrangementView）

**Status:** pending

**Dependencies:** 2 ✓, 4

**Priority:** high

**Description:** 实现核心座位图的可视化渲染，支持不同布局类型的绘制

**Details:**

1. 创建 SeatingArrangementView.axaml
2. 调用 GenerateSeatingAsync 生成座位安排
3. 座位图 Canvas 渲染（颜色区分空位/已分配/固定/冲突）
4. 学生列表侧边栏（已分配/未分配）
5. 支持 Grid/Polar/Freeform 三种布局渲染

**Test Strategy:**

手动测试：加载会场+学生 → 生成座位 → 验证渲染正确
