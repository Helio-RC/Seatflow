using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
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
            new("Avalonia UI", "12.0.3", "跨平台 UI 框架", "MIT", "https://github.com/AvaloniaUI/Avalonia"),
            new("CommunityToolkit.Mvvm", "8.4.2", "MVVM 源生成器", "MIT", "https://github.com/CommunityToolkit/dotnet"),
            new("FluentIcons.Avalonia", "2.1.326", "Fluent UI 系统图标", "MIT", "https://github.com/Seifzeid/FluentIcons.Avalonia"),
            new("EPPlus", "8.5.4", "Excel 读写", "PolyForm Noncommercial", "https://github.com/EPPlusSoftware/EPPlus"),
            new("CsvHelper", "33.1.0", "CSV 读写", "Apache 2.0 / MS-PL", "https://github.com/JoshClose/CsvHelper"),
            new("QuestPDF", "2026.2.4", "PDF 生成", "MIT (Community)", "https://github.com/QuestPDF/QuestPDF"),
            new("NLua", "1.7.8", "Lua 脚本引擎", "MIT", "https://github.com/NLua/NLua"),
            new("Serilog", "10.0.0", "结构化日志", "Apache 2.0", "https://github.com/serilog/serilog"),
        ];
    }

    [RelayCommand]
    private void OpenUrl (string url)
    {
        if (!string.IsNullOrWhiteSpace(url))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}

public class DependencyInfo
{
    public string Name { get; }
    public string Version { get; }
    public string Purpose { get; }
    public string License { get; }
    public string Url { get; }

    public DependencyInfo (string name , string version , string purpose , string license , string url)
    {
        Name = name;
        Version = version;
        Purpose = purpose;
        License = license;
        Url = url;
    }
}
