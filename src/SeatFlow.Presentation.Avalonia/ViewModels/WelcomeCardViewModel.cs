using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using Avalonia.Controls;
using AvaloniaApplication = Avalonia.Application;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 排座工作台空态欢迎卡（M3：Home 页移除后，欢迎语 / 快捷链接 / RELEASE 更新说明压缩到此卡）。
/// 数据来源：about.json（链接）+ 嵌入资源 release.md（更新说明，Markdig 渲染）。
/// </summary>
public partial class WelcomeCardViewModel : ViewModelBase
{
    private readonly IServiceProvider? _serviceProvider;

    public string GreetingLine { get; }
    public string VersionLabel { get; }

    public string DocsUrl { get; }
    public string QuickStartUrl { get; }
    public string FaqUrl { get; }

    public List<MdBlock> ReleaseBlocks { get; } = [];
    public bool HasReleaseNotes => ReleaseBlocks.Count > 0;

    public WelcomeCardViewModel(IDialogService dialog, IServiceProvider? serviceProvider = null) : base(dialog)
    {
        _serviceProvider = serviceProvider;

        var data = LoadAboutData();
        var userName = Environment.UserName;
        var hour = DateTime.Now.Hour;
        var template = hour switch
        {
            < 12 => Resources.Home_Greeting_Morning,
            < 18 => Resources.Home_Greeting_Afternoon,
            _ => Resources.Home_Greeting_Evening,
        };
        var personalized = string.Format(template, userName);
        var sep = CultureInfo.CurrentUICulture.Name.StartsWith("zh") ? "！" : "! ";
        GreetingLine = $"{personalized}{sep}{Resources.Home_Greeting}";

        VersionLabel = string.Format(Resources.Home_Version, VersionInfo.Version);

        DocsUrl = data.DocsUrl;
        QuickStartUrl = data.QuickStartUrl;
        FaqUrl = data.FaqUrl;

        ReleaseBlocks = LoadReleaseNotes();
    }

    // ═══════════════════════════════════════════════
    //  RELEASE.md 读取 + Markdig 渲染
    // ═══════════════════════════════════════════════

    private static List<MdBlock> LoadReleaseNotes()
    {
        var assembly = typeof(WelcomeCardViewModel).Assembly;
        const string resourceName = "SeatFlow.Presentation.Avalonia.Data.release.md";

        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                return [];

            using var reader = new StreamReader(stream);
            return MarkdownRenderer.Render(reader.ReadToEnd());
        }
        catch
        {
            return [];
        }
    }

    // ═══════════════════════════════════════════════
    //  打开链接
    // ═══════════════════════════════════════════════

    [RelayCommand]
    private async Task OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (AvaloniaApplication.Current?.ApplicationLifetime is
            global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } mainWindow)
        {
            var launcher = TopLevel.GetTopLevel(mainWindow)?.Launcher;
            if (launcher is not null)
            {
                await launcher.LaunchUriAsync(new Uri(url));
                return;
            }
        }

        // 兜底：无头环境 / 浏览器（WASM）→ 平台 URL 打开器
        var opener = _serviceProvider?.GetService<IUrlOpener>();
        opener?.OpenUrl(url);
    }

    // ═══════════════════════════════════════════════
    //  about.json 读取
    // ═══════════════════════════════════════════════

    private static AboutPageData LoadAboutData()
    {
        var assembly = typeof(WelcomeCardViewModel).Assembly;
        const string resourceName = "SeatFlow.Presentation.Avalonia.Data.about.json";

        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                return new AboutPageData();

            var all = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, AboutPageData>>(stream, _jsonOptions)
                      ?? new Dictionary<string, AboutPageData>();

            var culture = CultureInfo.CurrentUICulture;
            if (all.TryGetValue(culture.Name, out var match)) return match;
            if (all.TryGetValue(culture.TwoLetterISOLanguageName, out match)) return match;
            if (all.TryGetValue("zh-CN", out match)) return match;
            return all.Values.FirstOrDefault() ?? new AboutPageData();
        }
        catch
        {
            return new AboutPageData();
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class AboutPageData
    {
        public string DocsUrl { get; set; } = "";
        public string QuickStartUrl { get; set; } = "";
        public string FaqUrl { get; set; } = "";
    }
}
