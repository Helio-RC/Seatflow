# Task ID: 14

**Title:** 关于视图（AboutView）

**Status:** done

**Dependencies:** 2 ✓

**Priority:** low

**Description:** 实现关于页面，展示应用信息、版本号和依赖库列表。所有内容绑定到 ViewModel，AXAML 零硬编码。

**Details:**

1. AboutViewModel 属性全部通过绑定展示：AppName/Version/Description/RuntimeVersion/AvaloniaVersion/ProjectUrl/License/Copyright
2. Version 从 AssemblyInformationalVersionAttribute 反射获取
3. RuntimeVersion 从 RuntimeInformation.FrameworkDescription 获取
4. AvaloniaVersion 从 Avalonia.Application 程序集反射获取
5. Dependencies 列表（DependencyInfo: Name/Version/Purpose）通过 DataTemplate 展示
6. 卡式布局：FluentIcon 图标 + 应用名称/版本 → 描述 → 运行环境卡片 → 核心依赖卡片 → 链接/版权
7. 使用 AvaloniaApplication 别名解决与项目命名空间冲突

**Test Strategy:**

dotnet build + dotnet test (116/116) 全部通过
