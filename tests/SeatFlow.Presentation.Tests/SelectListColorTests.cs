using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Styling;
using FluentAssertions;
using AvaloniaApplication = Avalonia.Application;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 选择列表（ListBox.sf-select）选中底色回归（用户指定 #90A8C0）：
/// Avalonia 12 Fluent 的 ListBoxItem 状态样式直接设置模板内 ContentPresenter#PART_ContentPresenter
/// 的背景；若选择器命中已不存在的模板部件（历史写法 Border#PART_BackgroundBorder），
/// 选中态会回退到 Fluent 默认强调色底。本测试渲染真实选中项并采样背景像素。
/// </summary>
public class SelectListColorTests
{
    [AvaloniaFact]
    public void 选择列表_选中项背景为用户指定蓝灰()
    {
        AvaloniaApplication.Current!.RequestedThemeVariant = ThemeVariant.Light;
        try
        {
            var list = new ListBox
            {
                Classes = { "sf-select" },
                ItemsSource = new[] { "甲", "乙" },
                SelectedIndex = 0,
                Width = 160,
            };
            var window = new Window { Width = 300, Height = 200, Content = list };
            window.Show();
            window.UpdateLayout();

            var item = list.GetRealizedContainers().OfType<ListBoxItem>().First();
            var point = item.TranslatePoint(
                new Point(item.Bounds.Width - 10, item.Bounds.Height / 2),
                window);
            point.Should().NotBeNull();

            var frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            using var fb = frame!.Lock();
            var o = ((int)point!.Value.Y * fb.RowBytes) + ((int)point.Value.X * 4);
            var c0 = System.Runtime.InteropServices.Marshal.ReadByte(fb.Address, o);
            var c1 = System.Runtime.InteropServices.Marshal.ReadByte(fb.Address, o + 1);
            var c2 = System.Runtime.InteropServices.Marshal.ReadByte(fb.Address, o + 2);
            var (r, g, b) = fb.Format == PixelFormat.Bgra8888 ? (c2, c1, c0) : (c0, c1, c2);

            r.Should().BeInRange(0x90 - 8, 0x90 + 8, "选中底应接近 #90A8C0 的 R 分量");
            g.Should().BeInRange(0xA8 - 8, 0xA8 + 8, "选中底应接近 #90A8C0 的 G 分量");
            b.Should().BeInRange(0xC0 - 8, 0xC0 + 8, "选中底应接近 #90A8C0 的 B 分量");

            window.Close();
        }
        finally
        {
            AvaloniaApplication.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }
}
