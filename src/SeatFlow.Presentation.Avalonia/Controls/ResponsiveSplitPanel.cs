using System;
using Avalonia;
using Avalonia.Controls;

namespace SeatFlow.Presentation.Avalonia.Controls;

/// <summary>
/// 两栏/单栏自适应面板：宽屏时左右分栏（可指定固定宽度的一侧），窄屏时纵向堆叠。
/// 用于 OOBE 名单/会场等「左侧列表 + 右侧编辑区」布局在手机/紧凑档切换为单栏。
/// </summary>
public sealed class ResponsiveSplitPanel : Panel
{
    public static readonly StyledProperty<bool> IsNarrowProperty =
        AvaloniaProperty.Register<ResponsiveSplitPanel, bool>(nameof(IsNarrow));

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ResponsiveSplitPanel, double>(nameof(Spacing));

    public static readonly StyledProperty<double> FixedWidthProperty =
        AvaloniaProperty.Register<ResponsiveSplitPanel, double>(nameof(FixedWidth));

    public static readonly StyledProperty<bool> FixedIsSecondProperty =
        AvaloniaProperty.Register<ResponsiveSplitPanel, bool>(nameof(FixedIsSecond));

    static ResponsiveSplitPanel()
    {
        AffectsMeasure<ResponsiveSplitPanel>(
            IsNarrowProperty,
            SpacingProperty,
            FixedWidthProperty,
            FixedIsSecondProperty);
        AffectsArrange<ResponsiveSplitPanel>(
            IsNarrowProperty,
            SpacingProperty,
            FixedWidthProperty,
            FixedIsSecondProperty);
    }

    /// <summary>是否切换为纵向单栏。</summary>
    public bool IsNarrow
    {
        get => GetValue(IsNarrowProperty);
        set => SetValue(IsNarrowProperty, value);
    }

    /// <summary>两栏间距 / 堆叠行距。</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>固定宽度一侧的宽度（宽屏时生效）。</summary>
    public double FixedWidth
    {
        get => GetValue(FixedWidthProperty);
        set => SetValue(FixedWidthProperty, value);
    }

    /// <summary>固定宽度的是第二个子元素（true）还是第一个子元素（false）。</summary>
    public bool FixedIsSecond
    {
        get => GetValue(FixedIsSecondProperty);
        set => SetValue(FixedIsSecondProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0) return default;

        if (IsNarrow || double.IsInfinity(availableSize.Width))
        {
            double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
            double height = 0;
            for (int i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                child.Measure(new Size(width, double.PositiveInfinity));
                height += child.DesiredSize.Height;
                if (i < Children.Count - 1) height += Spacing;
                width = Math.Max(width, child.DesiredSize.Width);
            }

            return new Size(
                double.IsInfinity(availableSize.Width) ? width : availableSize.Width,
                height);
        }

        if (Children.Count == 1)
        {
            Children[0].Measure(availableSize);
            return Children[0].DesiredSize;
        }

        var (fixedIndex, flexibleIndex) = GetIndices();
        double fixedWidth = Math.Clamp(FixedWidth, 0, availableSize.Width);
        double flexibleWidth = Math.Max(0, availableSize.Width - fixedWidth - Spacing);

        var fixedChild = Children[fixedIndex];
        var flexibleChild = Children[flexibleIndex];
        fixedChild.Measure(new Size(fixedWidth, double.PositiveInfinity));
        flexibleChild.Measure(new Size(flexibleWidth, double.PositiveInfinity));

        double maxHeight = Math.Max(fixedChild.DesiredSize.Height, flexibleChild.DesiredSize.Height);
        return new Size(availableSize.Width, maxHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0) return finalSize;

        if (IsNarrow)
        {
            double y = 0;
            foreach (var child in Children)
            {
                double height = child.DesiredSize.Height;
                child.Arrange(new Rect(0, y, finalSize.Width, height));
                y += height + Spacing;
            }

            return finalSize;
        }

        if (Children.Count == 1)
        {
            Children[0].Arrange(new Rect(finalSize));
            return finalSize;
        }

        var (fixedIndex, flexibleIndex) = GetIndices();
        double fixedWidth = Math.Clamp(FixedWidth, 0, finalSize.Width);
        double flexibleWidth = Math.Max(0, finalSize.Width - fixedWidth - Spacing);

        double fixedX = FixedIsSecond ? finalSize.Width - fixedWidth : 0;
        double flexibleX = FixedIsSecond ? 0 : fixedWidth + Spacing;

        Children[fixedIndex].Arrange(new Rect(fixedX, 0, fixedWidth, finalSize.Height));
        Children[flexibleIndex].Arrange(new Rect(flexibleX, 0, flexibleWidth, finalSize.Height));
        return finalSize;
    }

    private (int Fixed, int Flexible) GetIndices()
        => FixedIsSecond ? (1, 0) : (0, 1);
}
