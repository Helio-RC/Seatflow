using System;
using Avalonia.Controls;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 对话框尺寸钳制：窄窗口/视口下按宿主尺寸收缩对话框，避免内容溢出屏幕。
/// 桌面壳按主窗口尺寸钳制 <see cref="Window"/>；浏览器壳（WASM）按视口钳制 overlay 内容。
/// </summary>
public static class DialogSizing
{
    /// <summary>对话框最小宽度（低于此值视为设计缺陷）。</summary>
    public const double MinDialogWidth = 280;

    /// <summary>对话框最小高度。</summary>
    public const double MinDialogHeight = 240;

    /// <summary>桌面：把对话框宽/高收紧到宿主窗口内（留出边距；NaN 表示不约束的维度）。</summary>
    public static void ClampToOwner(Window dialog, Window owner)
    {
        var ownerWidth = Resolve(owner.Width, owner.Bounds.Width);
        var ownerHeight = Resolve(owner.Height, owner.Bounds.Height);

        if (ownerWidth > 0 && !double.IsNaN(dialog.Width))
            dialog.Width = Math.Max(MinDialogWidth, Math.Min(dialog.Width, ownerWidth - 32));

        if (ownerHeight <= 0) return;

        if (!double.IsNaN(dialog.Height))
        {
            // 固定高度窗口（如 UpdateDialogWindow）：直接钳制高度
            dialog.Height = Math.Max(MinDialogHeight, Math.Min(dialog.Height, ownerHeight - 64));
        }
        else
        {
            // SizeToContent="Height" 窗口：Height 为 NaN，钳制其高度上限，避免长内容撑出屏幕
            dialog.MaxHeight = Math.Max(MinDialogHeight, ownerHeight - 64);
        }
    }

    /// <summary>浏览器：计算 overlay 内容可用的最大宽度（视口宽 - 边距）。</summary>
    public static double ResolveMaxWidth(double designWidth, double viewportWidth)
        => viewportWidth > 0
            ? Math.Max(MinDialogWidth, Math.Min(designWidth, viewportWidth - 32))
            : designWidth;

    /// <summary>浏览器：计算 overlay 内容可用的最大高度（视口高 - 边距）。</summary>
    public static double ResolveMaxHeight(double designHeight, double viewportHeight)
        => viewportHeight > 0
            ? Math.Max(MinDialogHeight, Math.Min(designHeight, viewportHeight - 48))
            : designHeight;

    private static double Resolve(double preferred, double bounds)
        => !double.IsNaN(preferred) && preferred > 0 ? preferred : bounds;
}
