using System.Collections.Generic;
using System.Reflection;
using AvaloniaApplication = Avalonia.Application;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    public string AppName { get; } = "A_Pair";
    public string Version { get; }
    public string Description { get; } = "跨平台桌面座位安排与轮换系统，支持多种布局、策略引擎、插件扩展和数据导入导出。";
    public string RuntimeVersion { get; }
    public string AvaloniaVersion { get; }
    public string ProjectUrl { get; } = "https://github.com/Helio-RC/A_Pair";
    public string License { get; } = "MIT License";
    public string Copyright { get; } = $"© {System.DateTime.Now.Year} A_Pair Contributors";

    public List<DependencyInfo> Dependencies { get; }

    public AboutViewModel ()
    {
        var assembly = Assembly.GetEntryAssembly();
        Version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? assembly?.GetName()?.Version?.ToString()
                  ?? "1.0.0";

        RuntimeVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        AvaloniaVersion = typeof(AvaloniaApplication).Assembly.GetName().Version?.ToString() ?? "";

        Dependencies =
        [
            new("Avalonia UI", "12.0.2", "跨平台 UI 框架"),
            new("CommunityToolkit.Mvvm", "8.4.2", "MVVM 源生成器"),
            new("FluentIcons.Avalonia", "2.1.325", "Fluent UI 系统图标"),
            new("EPPlus", "8.5.4", "Excel 读写"),
            new("CsvHelper", "33.1.0", "CSV 读写"),
            new("QuestPDF", "2026.2.4", "PDF 生成"),
            new("NLua", "1.7.8", "Lua 脚本引擎"),
            new("Serilog", "10.0.0", "结构化日志"),
        ];
    }
}

public class DependencyInfo
{
    public string Name { get; }
    public string Version { get; }
    public string Purpose { get; }

    public DependencyInfo (string name , string version , string purpose)
    {
        Name = name;
        Version = version;
        Purpose = purpose;
    }
}
