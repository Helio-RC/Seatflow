using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using SeatFlow.Core.Models;
using AvaloniaApplication = Avalonia.Application;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 主题色应用器：把「默认（#83B6DE）/ 系统」主题色写入应用明/暗主题字典，
/// 并派生悬停 / 按下 / 浅底颜色与**按对比度自动选择的前景色**，
/// 保证主题色按钮上的文字始终可读（浅色主题色 → 深色文字，深色主题色 → 白字）。
/// </summary>
public static class AccentColorApplier
{
    /// <summary>内置默认主题色。</summary>
    public static readonly Color DefaultAccent = Color.Parse("#83B6DE");

    /// <summary>浅底上的深色前景（与 SfTextColor 一致）。</summary>
    private static readonly Color LightForeground = Color.Parse("#16202B");

    /// <summary>深底上的白色前景。</summary>
    private static readonly Color DarkForeground = Color.Parse("#FFFFFF");

    /// <summary>按设置应用主题色（系统模式取不到系统色时回退默认）。</summary>
    public static void Apply(AccentColorMode mode)
    {
        var accent = mode == AccentColorMode.System
            ? TryGetSystemAccent() ?? DefaultAccent
            : DefaultAccent;
        ApplyColor(accent);
    }

    /// <summary>把指定主题色写入明/暗主题字典（含派生色与对比前景）。</summary>
    public static void ApplyColor(Color accent)
    {
        if (AvaloniaApplication.Current is not { } app) return;
        if (app.Resources is not ResourceDictionary root) return;

        var foreground = ChooseForeground(accent);
        ApplyToVariant(root, ThemeVariant.Light, accent, foreground);
        ApplyToVariant(root, ThemeVariant.Dark, accent, foreground);
    }

    private static void ApplyToVariant(ResourceDictionary root, ThemeVariant variant, Color accent, Color foreground)
    {
        if (root.ThemeDictionaries[variant] is not ResourceDictionary dict) return;
        var isLight = variant == ThemeVariant.Light;

        dict["SfAccentColor"] = accent;
        dict["SfAccentHoverColor"] = Darken(accent, 0.10);
        dict["SfAccentStrongColor"] = Darken(accent, 0.22);
        dict["SfAccentSoftColor"] = isLight ? Mix(accent, Colors.White, 0.86) : Mix(accent, Colors.Black, 0.78);
        dict["SfAccentFgColor"] = foreground;

        // Fluent 强调色派生（影响原生控件的高亮/选中）
        dict["SystemAccentColor"] = accent;
        dict["SystemAccentColorLight1"] = isLight ? Darken(accent, 0.08) : Lighten(accent, 0.12);
        dict["SystemAccentColorLight2"] = isLight ? Darken(accent, 0.16) : Lighten(accent, 0.24);
        dict["SystemAccentColorLight3"] = isLight ? Darken(accent, 0.26) : Lighten(accent, 0.36);
        dict["SystemAccentColorDark1"] = Darken(accent, 0.12);
        dict["SystemAccentColorDark2"] = Darken(accent, 0.24);
        dict["SystemAccentColorDark3"] = Darken(accent, 0.36);
    }

    /// <summary>按相对亮度选择可读前景（阈值取浅底深字 / 深底白字中对比更优的一侧）。</summary>
    public static Color ChooseForeground(Color background)
        => RelativeLuminance(background) > 0.42 ? LightForeground : DarkForeground;

    /// <summary>WCAG 相对亮度（sRGB 线性化）。</summary>
    public static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var s = value / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static Color Darken(Color color, double amount) => Mix(color, Colors.Black, amount);

    private static Color Lighten(Color color, double amount) => Mix(color, Colors.White, amount);

    private static Color Mix(Color a, Color b, double ratio)
        => Color.FromRgb(
            (byte)Math.Round((a.R * (1 - ratio)) + (b.R * ratio)),
            (byte)Math.Round((a.G * (1 - ratio)) + (b.G * ratio)),
            (byte)Math.Round((a.B * (1 - ratio)) + (b.B * ratio)));

    private static Color? TryGetSystemAccent()
    {
        try
        {
            return AvaloniaApplication.Current?.PlatformSettings?.GetColorValues()?.AccentColor1;
        }
        catch
        {
            return null;
        }
    }
}
