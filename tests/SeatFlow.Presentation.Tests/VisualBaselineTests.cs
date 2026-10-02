using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Telemetry;
using SeatFlow.Presentation.Avalonia.Behaviors;
using SeatFlow.Presentation.Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Presentation.Avalonia.Views;
using AvaloniaApplication = Avalonia.Application;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 视觉回归基线捕获（M6）：Headless + Skia 真实绘制，输出关键页明 / 暗 PNG。
/// <para>
/// 基线图片不入库（<c>docs/ui-refactor/assets/</c> 已 gitignore），本测试只负责生成与
/// 可渲染性断言（帧非空、文件可写）。人工对比与复现方式见
/// <c>docs/ui-refactor/08-implementation-log.md</c> 的 M6 小节。
/// 输出目录优先取环境变量 <c>SEATFLOW_BASELINE_DIR</c>，否则为仓库内
/// <c>docs/ui-refactor/assets/after/baselines/</c>。
/// </para>
/// </summary>
public class VisualBaselineTests
{
    private static readonly ThemeVariant[] Variants = [ThemeVariant.Light, ThemeVariant.Dark];

    private static string BaselineDir()
    {
        var custom = Environment.GetEnvironmentVariable("SEATFLOW_BASELINE_DIR");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            Directory.CreateDirectory(custom);
            return custom;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "SeatFlow.slnx")))
            current = current.Parent;

        var dir = Path.Combine(current?.FullName ?? Path.GetTempPath(), "docs", "ui-refactor", "assets", "after", "baselines");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Capture(string name, ThemeVariant variant, Control content)
    {
        AvaloniaApplication.Current!.RequestedThemeVariant = variant;

        var window = new Window { Width = 1200, Height = 800, Content = content };
        window.Show();
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull($"「{name}」在 {variant} 主题下应能渲染出帧");
        frame!.PixelSize.Width.Should().BeGreaterThan(0);

        var path = Path.Combine(BaselineDir(), $"{name}-{(variant == ThemeVariant.Dark ? "dark" : "light")}.png");
        frame.Save(path, new global::Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        File.Exists(path).Should().BeTrue();
        new FileInfo(path).Length.Should().BeGreaterThan(0, "基线 PNG 不应为空文件");

        window.Close();
    }

    private static SeatingCanvas CreateSeatingCanvas()
    {
        var seats = new List<SeatVisual>();
        for (var r = 0; r < 4; r++)
        {
            for (var c = 0; c < 6; c++)
            {
                var index = (r * 6) + c;
                seats.Add(new SeatVisual(
                    $"R{r}C{c}",
                    c * 90,
                    r * 70,
                    64,
                    44,
                    IsOccupied: index < 14,
                    IsFixed: index is 0 or 5,
                    Label: index < 14 ? $"学生{index + 1:00}" : null,
                    SeatLabel: $"R{r + 1}C{c + 1}",
                    StudentId: index < 14 ? $"s{index}" : null,
                    IsSwapSource: index == 7,
                    IsDataStale: index == 9));
            }
        }

        return new SeatingCanvas
        {
            Snapshot = new SeatLayoutSnapshot(
                seats,
                24 + (6 * 90),
                24 + (4 * 70),
                Version: 1,
                Overlays:
                [
                    new BoardOverlay(150, 0, 240, 36, "讲台", IsRound: true),
                    new BoardOverlay(560, 24, 40, 90, "门", IsDoor: true),
                ]),
        };
    }

    private static MemberManagementViewModel CreateMemberVm() => new(
        Substitute.For<IApplicationFacade>(),
        Substitute.For<IFileService>(),
        NullDialogService.Instance,
        Substitute.For<IUrlOpener>(),
        new DialogGate(),
        new ShellLayoutService());

    private static SettingsViewModel CreateSettingsVm() => new(
        Substitute.For<IApplicationFacade>(),
        NullDialogService.Instance,
        Substitute.For<IOnboardingService>(),
        Substitute.For<IFileService>(),
        Substitute.For<ITelemetryService>(),
        Substitute.For<IUpdateService>(),
        Substitute.For<IUrlOpener>(),
        Substitute.For<IServiceProvider>(),
        new KeyboardShortcutHandler(),
        new DialogGate(),
        new ShellLayoutService());

    [AvaloniaFact]
    public void 排座画布_明暗基线()
    {
        try
        {
            foreach (var variant in Variants)
                Capture("seating-canvas", variant, CreateSeatingCanvas());
        }
        finally
        {
            AvaloniaApplication.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    [AvaloniaFact]
    public void 人员名单页_明暗基线()
    {
        try
        {
            foreach (var variant in Variants)
            {
                var vm = CreateMemberVm();
                vm.SeedGuideData();
                Capture("member-management", variant, new MemberManagementView { DataContext = vm });
            }
        }
        finally
        {
            AvaloniaApplication.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    [AvaloniaFact]
    public void 设置页_明暗基线()
    {
        try
        {
            foreach (var variant in Variants)
                Capture("settings", variant, new SettingsView { DataContext = CreateSettingsVm() });
        }
        finally
        {
            AvaloniaApplication.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    [AvaloniaFact]
    public void 关于页_明暗基线()
    {
        try
        {
            foreach (var variant in Variants)
                Capture("about", variant, new AboutView { DataContext = new AboutViewModel(NullDialogService.Instance) });
        }
        finally
        {
            AvaloniaApplication.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }
}
