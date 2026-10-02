using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Styling;
using FluentAssertions;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using AvaloniaApplication = Avalonia.Application;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 主题色按钮（sf-primary）前景对比度回归（用户实测反馈：Windows 浅色下文字呈深色）：
/// Fluent 按钮模板直接用模板内样式设置 ContentPresenter.Foreground，会压过 Button.Foreground；
/// 本测试采样渲染像素，保证静止与悬停状态下文字均为强调前景色（浅色主题 = 白）。
/// </summary>
public class AccentButtonForegroundTests
{
    private static (int Dark, int Light) SampleButton(Window window, Button button)
    {
        var frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
        using var fb = frame!.Lock();
        var bytes = new byte[fb.RowBytes * fb.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(fb.Address, bytes, 0, bytes.Length);
        var isBgra = fb.Format == PixelFormat.Bgra8888;

        var b = button.Bounds;
        var dark = 0;
        var light = 0;
        for (var y = (int)b.Y + 4; y < (int)b.Bottom - 4; y++)
        {
            for (var x = (int)b.X + 4; x < (int)b.Right - 4; x++)
            {
                var o = (y * fb.RowBytes) + (x * 4);
                var c0 = bytes[o];
                var c1 = bytes[o + 1];
                var c2 = bytes[o + 2];
                var r = isBgra ? c2 : c0;
                var g = c1;
                var bl = isBgra ? c0 : c2;
                if (r < 80 && g < 80 && bl < 80) dark++;
                if (r > 200 && g > 200 && bl > 200) light++;
            }
        }
        return (dark, light);
    }

    [AvaloniaFact]
    public void 主按钮_静止与悬停_文字均为强调前景色()
    {
        AvaloniaApplication.Current!.RequestedThemeVariant = ThemeVariant.Light;
        try
        {
            var button = new Button
            {
                Classes = { "sf-btn", "sf-primary" },
                // 与生产按钮一致的结构：图标 + `.sf-sm` 文本（该文本类显式设置 SfTextBrush，
                // 曾导致主题色按钮文字仍然为黑色）
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
                Width = 120,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var window = new Window { Width = 400, Height = 200, Content = button };
            window.Show();
            window.UpdateLayout();

            var still = SampleButton(window, button);
            still.Dark.Should().BeLessThan(20, "静止态不应出现深色文字（前景应为强调前景色）");
            still.Light.Should().BeGreaterThan(30, "应存在浅色文字像素");

            var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
            window.MouseMove(center!.Value, RawInputModifiers.None);
            window.UpdateLayout();

            var hovered = SampleButton(window, button);
            hovered.Dark.Should().BeLessThan(20, "悬停态不应出现深色文字");
            hovered.Light.Should().BeGreaterThan(30, "悬停态应存在浅色文字像素");

            window.Close();
        }
        finally
        {
            AvaloniaApplication.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }
}
