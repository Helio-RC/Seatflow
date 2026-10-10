<!-- markdownlint-disable MD033 MD041 -->
<div align="center">

<p align="center">
  <picture><source media="(prefers-color-scheme: dark)" srcset="https://shieldcn.dev/header/grid.svg?title=SeatFlow+%E2%80%94+%E5%BA%A7%E6%B5%81&amp;subtitle=%E8%B7%A8%E5%B9%B3%E5%8F%B0%E5%BA%A7%E4%BD%8D%E5%AE%89%E6%8E%92%E4%B8%8E%E8%BD%AE%E6%8D%A2%E7%B3%BB%E7%BB%9F%EF%BC%88%E6%A1%8C%E9%9D%A2+%2B+%E6%B5%8F%E8%A7%88%E5%99%A8%EF%BC%89&amp;mode=dark&amp;theme=zinc&amp;image=https%3A%2F%2Fraw.githubusercontent.com%2FHelio-RC%2FSeatflow%2Fmain%2FAssets%2FSF_icon_mini_background.png" /><img alt="SeatFlow — 座流：跨平台座位安排与轮换系统（桌面 + 浏览器）" src="https://shieldcn.dev/header/grid.svg?title=SeatFlow+%E2%80%94+%E5%BA%A7%E6%B5%81&amp;subtitle=%E8%B7%A8%E5%B9%B3%E5%8F%B0%E5%BA%A7%E4%BD%8D%E5%AE%89%E6%8E%92%E4%B8%8E%E8%BD%AE%E6%8D%A2%E7%B3%BB%E7%BB%9F%EF%BC%88%E6%A1%8C%E9%9D%A2+%2B+%E6%B5%8F%E8%A7%88%E5%99%A8%EF%BC%89&amp;mode=light&amp;theme=zinc&amp;image=https%3A%2F%2Fraw.githubusercontent.com%2FHelio-RC%2FSeatflow%2Fmain%2FAssets%2FSF_icon_mini_background.png" /></picture>
</p>

<a href="https://github.com/Helio-RC/Seatflow/releases"><picture><source media="(prefers-color-scheme: dark)" srcset="https://shieldcn.dev/github/release/Helio-RC/Seatflow.svg?variant=secondary&theme=zinc&mode=dark" /><img alt="Release" src="https://shieldcn.dev/github/release/Helio-RC/Seatflow.svg?variant=secondary&theme=zinc&mode=light" /></picture></a>
<a href="https://github.com/Helio-RC/Seatflow/stargazers"><picture><source media="(prefers-color-scheme: dark)" srcset="https://shieldcn.dev/github/stars/Helio-RC/Seatflow.svg?variant=secondary&theme=zinc&mode=dark" /><img alt="GitHub stars" src="https://shieldcn.dev/github/stars/Helio-RC/Seatflow.svg?variant=secondary&theme=zinc&mode=light" /></picture></a>
<a href="https://github.com/Helio-RC/Seatflow/actions/workflows/unit-tests.yml"><picture><source media="(prefers-color-scheme: dark)" srcset="https://shieldcn.dev/github/ci/Helio-RC/Seatflow.svg?workflow=unit-tests.yml&variant=secondary&theme=zinc&mode=dark" /><img alt="CI status" src="https://shieldcn.dev/github/ci/Helio-RC/Seatflow.svg?workflow=unit-tests.yml&variant=secondary&theme=zinc&mode=light" /></picture></a>
<a href="https://github.com/Helio-RC/Seatflow/blob/main/LICENSE"><picture><source media="(prefers-color-scheme: dark)" srcset="https://shieldcn.dev/github/license/Helio-RC/Seatflow.svg?variant=secondary&theme=zinc&mode=dark" /><img alt="License" src="https://shieldcn.dev/github/license/Helio-RC/Seatflow.svg?variant=secondary&theme=zinc&mode=light" /></picture></a>
<a href="https://dotnet.microsoft.com/download/dotnet/10.0"><picture><source media="(prefers-color-scheme: dark)" srcset="https://shieldcn.dev/badge/.NET-10-512bd4.svg?logo=dotnet&variant=secondary&theme=zinc&mode=dark" /><img alt=".NET 10" src="https://shieldcn.dev/badge/.NET-10-512bd4.svg?logo=dotnet&variant=secondary&theme=zinc&mode=light" /></picture></a>
<a href="https://qm.qq.com/q/y9D63B0dYk"><picture><source media="(prefers-color-scheme: dark)" srcset="https://shieldcn.dev/badge/QQ-761175549.svg?logo=qq&variant=secondary&theme=zinc&mode=dark" /><img alt="QQ 群" src="https://shieldcn.dev/badge/QQ-761175549.svg?logo=qq&variant=secondary&theme=zinc&mode=light" /></picture></a>

导入名单，选好布局，一键生成座位表 —— 11 条排座策略、拖拽微调、历史快照回滚与多格式导出。

🌐 在线体验 —— [https://online.seatflow.work](https://online.seatflow.work)

</div>

## ✨ 核心功能

- [x] **多格式数据导入** — CSV、Excel（XLSX）、JSON 学生名单导入
- [x] **智能排座引擎** — 11 条内置策略管道执行（6 独立 + 5 依赖）：固定座位、身高优先、前排轮换、蛇形顺序、性别限制、同桌分组、同桌性别搭配、不为同桌、同桌上一次、随机填充、碎片整理；策略按优先级 Fill-in-Order 模型执行
- [x] **手动微调** — 拖拽交换座位，全功能撤销/重做
- [x] **多种布局** — 网格、环形/扇形、自由点教室布局；支持障碍物（柱子、讲台）
- [x] **多格式导出** — Excel、CSV、PDF、图片导出座位表（PDF/图片仅桌面版）
- [x] **历史快照** — 手动保存排座快照，支持回滚到任意历史版本
- [x] **配置驱动** — 策略优先级、布局参数、导出选项均可配置
- [x] **浏览器版（Web/WASM）** — 共享 UI 外壳，无需安装即可在浏览器中使用；数据存储在 IndexedDB，支持 Excel/CSV 导入导出与 `.seatsets` 打包迁移
- [x] **跨平台** — Windows / Linux 原生运行（macOS 支持计划中，暂无安装包）；Web/WASM 浏览器版已正式部署（https://online.seatflow.work）

---

## 🚀 快速开始

> [!IMPORTANT]
> **环境要求**
> - [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
> - Windows 11 / Linux Ubuntu 22.04+（或其他 Linux 发行版）。macOS 支持计划中，暂无安装包。

**构建与运行（桌面版）**

```bash
git clone https://github.com/Helio-RC/Seatflow.git
cd SeatFlow
dotnet build
dotnet run --project src/SeatFlow.Desktop
```

**构建浏览器版（Web/WASM）**

```bash
dotnet workload install wasm-tools        # 一次性前置
dotnet publish src/SeatFlow.Browser -c Release
# 产物：src/SeatFlow.Browser/bin/Release/net10.0-browser/publish/wwwroot（纯静态站点）
```

**运行测试**

```bash
dotnet test
```

## 🧱 技术概要

.NET 10 + Avalonia 12 + CommunityToolkit.Mvvm，分层架构，外观模式统一入口，命令模式实现撤销/重做。

```
SeatFlow.Desktop（桌面壳）    SeatFlow.Browser（WASM 静态站）
        └──────────┬──────────┘
Presentation.Avalonia（共享 UI 库，net10.0 / net10.0-browser）
        ↓  IApplicationFacade
Application                 ← 编排、策略管道、命令栈
   ↓            ↓
Core           Infrastructure
领域模型        文件 I/O、布局生成器
策略接口        导出器、仓库、迁移、存储抽象（ILocalDataStore）
```

| 层 | 职责 |
| --- | --- |
| **Core** | 领域实体（`Student`, `Seat`）、策略接口、领域服务 |
| **Application** | 外观模式入口、策略管道、撤销/重做 |
| **Infrastructure** | CSV/Excel/JSON 导入导出、网格/环形/自由布局构建、PDF/图片导出、文件版本迁移 |
| **Presentation** | Avalonia 12 共享 UI 库（MVVM、编译绑定）；由 `SeatFlow.Desktop`（桌面壳）与 `SeatFlow.Browser`（WASM 静态站）两个启动壳承载 |

## 🤝 反馈与贡献

- **Bug 反馈**：请在 [GitHub Issues](https://github.com/Helio-RC/Seatflow/issues) 提交，附上操作系统版本和复现步骤，最好能附上日志
- **功能建议**：欢迎提交 Feature Request
- **新策略请求**：使用 [New Strategy issue 模板](.github/ISSUE_TEMPLATE/new-strategy.md) 描述排座策略（内置实现，无插件机制；2.0.0 起插件系统已移除）
- **界面美化**：初代开发者审美不好，欢迎各位大能贡献 UI 设计和图标资源
- **参与开发**：参见 [CONTRIBUTING.md](CONTRIBUTING.md) 了解构建环境、项目结构和编码规范
- **AI 辅助开发**：本项目使用 Claude Code & Deepseek V4 preview 辅助开发。项目级 AI 配置位于 [AGENTS.md](AGENTS.md)（根 `CLAUDE.md` 为指向它的指针），包含架构约定、代码模式和开发命令。建议 AI 开发者先阅读此文件和 [docs/adr/](docs/adr/) 中的架构决策记录
- **QQ 交流群**：[761175549](https://qm.qq.com/q/y9D63B0dYk) — 欢迎大家前来反馈问题、提出建议，或者一起吹水聊天！若 GitHub 的下载速度过慢，也可以进群下载

<p align="center">
  <a href="https://github.com/Helio-RC/Seatflow/graphs/contributors"><picture><source media="(prefers-color-scheme: dark)" srcset="https://shieldcn.dev/contributors/Helio-RC/Seatflow.svg?theme=zinc&amp;mode=dark" /><img alt="Contributors" src="https://shieldcn.dev/contributors/Helio-RC/Seatflow.svg?theme=zinc&amp;mode=light" /></picture></a>
</p>

## 📚 项目文档

| 文档 | 说明 |
| --- | --- |
| [docs/INDEX.md](docs/INDEX.md) | 文档导航地图（修改文档前先查阅联动规则） |
| [ARCHITECTURE.md](ARCHITECTURE.md) | 项目目标与架构设计 |
| [docs/WebDeployment.md](docs/WebDeployment.md) | Web/WASM 构建、部署与平台差异 |
| [docs/Phases.md](docs/Phases.md) | 实现阶段与详细规划 |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 开发环境搭建与参与指南 |
| [AGENTS.md](AGENTS.md) | AI 编码代理指南（根 `CLAUDE.md` 为指针） |
| [CHANGELOG.md](CHANGELOG.md) | 变更日志 |
| [docs/adr/](docs/adr/) | 架构决策记录 |
| [Design_Spec.md](docs/presentation/Design_Spec.md) | UI 设计规范 |
| [Fluent_Icons.md](docs/presentation/Fluent_Icons.md) | 图标参考 |

## ⚖️ 许可

MIT License © 2026 SeatFlow Contributors

---

<div align="center">
Made with ❤️ by SeatFlow Contributors
</div>
