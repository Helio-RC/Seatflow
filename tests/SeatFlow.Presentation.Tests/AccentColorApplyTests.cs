using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using FluentAssertions;
using SeatFlow.Presentation.Avalonia.Services;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using AvaloniaApplication = Avalonia.Application;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 主题色应用链路测试：写入应用明/暗主题字典（含对比前景），并在测试后恢复原值。
/// </summary>
public class AccentColorApplyTests
{
    private static readonly string[] AccentKeys =
    [
        "SfAccentColor", "SfAccentHoverColor", "SfAccentStrongColor", "SfAccentSoftColor", "SfAccentFgColor",
        "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
    ];

    [AvaloniaFact]
    public void 应用默认主题色_明暗字典均写入_前景按对比度选择_可恢复()
    {
        var root = (ResourceDictionary)AvaloniaApplication.Current!.Resources;
        var light = (ResourceDictionary)root.ThemeDictionaries[ThemeVariant.Light]!;
        var dark = (ResourceDictionary)root.ThemeDictionaries[ThemeVariant.Dark]!;
        var originalsLight = AccentKeys.ToDictionary(k => k, k => light[k]);
        var originalsDark = AccentKeys.ToDictionary(k => k, k => dark[k]);

        try
        {
            AccentColorApplier.ApplyColor(AccentColorApplier.DefaultAccent);

            light["SfAccentColor"].Should().Be(AccentColorApplier.DefaultAccent);
            dark["SfAccentColor"].Should().Be(AccentColorApplier.DefaultAccent);
            light["SfAccentFgColor"].Should().Be(AccentColorApplier.ChooseForeground(AccentColorApplier.DefaultAccent));
            dark["SfAccentFgColor"].Should().Be(AccentColorApplier.ChooseForeground(AccentColorApplier.DefaultAccent));
            light["SystemAccentColor"].Should().Be(AccentColorApplier.DefaultAccent);
        }
        finally
        {
            foreach (var (key, value) in originalsLight)
                if (value is not null) light[key] = value;
            foreach (var (key, value) in originalsDark)
                if (value is not null) dark[key] = value;
        }
    }

    [AvaloniaFact]
    public void 应用默认主题色后_主按钮渲染为浅蓝底与对比前景文字()
    {
        var root = (ResourceDictionary)AvaloniaApplication.Current!.Resources;
        var light = (ResourceDictionary)root.ThemeDictionaries[ThemeVariant.Light]!;
        var dark = (ResourceDictionary)root.ThemeDictionaries[ThemeVariant.Dark]!;
        var originalsLight = AccentKeys.ToDictionary(k => k, k => light[k]);
        var originalsDark = AccentKeys.ToDictionary(k => k, k => dark[k]);

        try
        {
            AccentColorApplier.ApplyColor(AccentColorApplier.DefaultAccent);

            var expectedFg = AccentColorApplier.ChooseForeground(AccentColorApplier.DefaultAccent);
            var button = new Button
            {
                Classes = { "sf-btn", "sf-primary" },
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FluentIcon { Icon = Icon.Save, FontSize = 14 },
                        new TextBlock
                        {
                            Classes = { "sf-sm" },
                            Text = "保存设置",
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    },
                },
                Width = 140,
                Height = 34,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var window = new Window { Width = 400, Height = 200, Content = button };
            window.Show();
            window.UpdateLayout();

            var frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            using var fb = frame!.Lock();
            var bytes = new byte[fb.RowBytes * fb.Size.Height];
            System.Runtime.InteropServices.Marshal.Copy(fb.Address, bytes, 0, bytes.Length);
            var isBgra = fb.Format == PixelFormat.Bgra8888;

            var accent = AccentColorApplier.DefaultAccent;
            var bgMatches = 0;
            var fgMatches = 0;
            var b = button.Bounds;
            for (var y = (int)b.Y + 3; y < (int)b.Bottom - 3; y++)
            {
                for (var x = (int)b.X + 3; x < (int)b.Right - 3; x++)
                {
                    var o = (y * fb.RowBytes) + (x * 4);
                    var r = isBgra ? bytes[o + 2] : bytes[o];
                    var g = bytes[o + 1];
                    var bl = isBgra ? bytes[o] : bytes[o + 2];
                    if (Math.Abs(r - accent.R) < 30 && Math.Abs(g - accent.G) < 30 && Math.Abs(bl - accent.B) < 30)
                        bgMatches++;
                    if (Math.Abs(r - expectedFg.R) < 60 && Math.Abs(g - expectedFg.G) < 60 && Math.Abs(bl - expectedFg.B) < 60)
                        fgMatches++;
                }
            }

            bgMatches.Should().BeGreaterThan(500, "按钮背景应为应用后的主题色 #83B6DE");
            fgMatches.Should().BeGreaterThan(20, "文字/图标应为按对比度选择的深色前景");
            window.Close();
        }
        finally
        {
            foreach (var (key, value) in originalsLight)
                if (value is not null) light[key] = value;
            foreach (var (key, value) in originalsDark)
                if (value is not null) dark[key] = value;
        }
    }
}
