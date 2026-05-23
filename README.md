# A_Pair

跨平台桌面座位安排与轮换系统。支持自动/手动排座、多数据源导入导出、策略引擎、插件扩展、历史快照回滚。

## 核心功能

- **多格式数据导入** — CSV、Excel（XLSX）、JSON 学生名单导入
- **智能排座引擎** — 四条内置策略管道执行：固定座位、同桌绑定、前排轮换、随机填充；后执行策略可覆盖先前分配
- **插件扩展** — 第三方可通过 DLL、Lua 脚本、C# 脚本编写自定义排座策略，拖入即用
- **手动微调** — 拖拽交换座位，全功能撤销/重做
- **多种布局** — 网格、环形/扇形、自由点教室布局；支持障碍物（柱子、讲台）
- **多格式导出** — Excel、CSV、PDF、图片导出座位表
- **历史快照** — 每次排座自动保存快照，支持回滚到任意历史版本
- **配置驱动** — 策略优先级、布局参数、导出选项均可配置
- **跨平台** — Windows / macOS / Linux 原生运行

## 开发路线图

当前已完成核心业务逻辑（领域模型、策略引擎、数据导入导出、插件系统、快照管理）和大部分 UI 页面。详见 [Phases.md](Phases.md)。

| 阶段 | 内容 | 状态 |
|------|------|------|
| Phase 1-3 | 领域建模、数据导入导出、内置策略 | 已完成 |
| Phase 4-5 | 插件系统、Lua/C# 脚本支持 | 已完成 |
| Phase 6 | 高级布局可视化、拖拽交互 | 进行中 |
| Phase 7 | CLI 工具 | 计划中 |
| Phase 8 | 测试覆盖与用户文档 | 计划中 |

## 反馈与贡献

- **Bug 反馈**：请在 GitHub Issues 提交，附上操作系统版本和复现步骤,最好能附上日志。
- **功能建议**：欢迎提交 Feature Request
- **插件开发**：参见 [A_Pair.Plugins.Sdk/docs/README.md](A_Pair.Plugins.Sdk/docs/README.md) 插件开发指南
- **参与开发**：参见 [CONTRIBUTING.md](CONTRIBUTING.md) 了解构建环境、项目结构和编码规范
- **AI 辅助开发**：本项目使用 Claude Code & Deepseek V4 preview 辅助开发。项目级 AI 配置位于 [CLAUDE.md](CLAUDE.md)，包含架构约定、代码模式和开发命令。建议 AI 开发者先阅读此文件和 [docs/adr/](docs/adr/) 中的架构决策记录

## 技术概要

.NET 10 + Avalonia 12 + CommunityToolkit.Mvvm，采用分层架构（Core → Application → Infrastructure → Presentation），[外观模式](https://en.wikipedia.org/wiki/Facade_pattern)统一入口，[命令模式](https://en.wikipedia.org/wiki/Command_pattern)实现撤销/重做。

## 项目文档

| 文档 | 说明 |
|------|------|
| [ARCHITECTURE.md](ARCHITECTURE.md) | 项目目标与架构设计 |
| [Phases.md](Phases.md) | 实现阶段与详细规划 |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 开发环境搭建与参与指南 |
| [CLAUDE.md](CLAUDE.md) | AI 编码助手配置 |
| [docs/adr/](docs/adr/) | 架构决策记录 |
| [Design_Spec.md](A_Pair.Presentation.Avalonia/docs/Design_Spec.md) | UI 设计规范 |
| [Fluent_Icons.md](A_Pair.Presentation.Avalonia/docs/Fluent_Icons.md) | 图标参考 |
| [Plugins.Sdk/README.md](A_Pair.Plugins.Sdk/docs/README.md) | 插件开发 SDK 文档 |
