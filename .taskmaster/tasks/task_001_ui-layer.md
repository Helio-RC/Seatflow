# Task ID: 1

**Title:** 基础设施搭建：DI 接入与项目引用

**Status:** done

**Dependencies:** None

**Priority:** high

**Description:** 将 Presentation.Avalonia 接入 Application 层：添加项目引用、配置 DI 容器、改造启动流程

**Details:**

1. 在 A_Pair.Presentation.Avalonia.csproj 中添加对 A_Pair.Application 的项目引用
2. 安装 NuGet 包：Microsoft.Extensions.DependencyInjection
3. 改造 Program.cs：创建 ServiceCollection，调用 services.AddA_PairApplication()
4. 改造 App.axaml.cs：通过 DI 解析 MainWindow 和 MainShellViewModel
5. 验证 dotnet build 通过

**Test Strategy:**

dotnet build 成功，启动应用验证 MainWindow 正常显示
