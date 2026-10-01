using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SeatFlow.Presentation.Avalonia.Controls;

/// <summary>
/// 自绘座位画布（06 计划 §5 方案 C；M0 骨架：渲染 + 快照驱动重绘，M1 补齐命中/拖拽/缩放/键盘）。
///
/// 设计约定：
/// - 渲染输入为不可变 <see cref="SeatLayoutSnapshot"/>；引用替换/版本变化 → <see cref="Visual.InvalidateVisual"/>；
/// - 不做逐座位控件，命中将采用几何计算（M1）；
/// - 主题色每次 <see cref="Render"/> 按当前主题变体解析并缓存，主题切换时失效重绘。
/// </summary>
public sealed class SeatingCanvas : Control
{
    /// <summary>板面四周留白（方向 B 方格纸画布）。</summary>
    public const double BoardMargin = 24;

    public static readonly StyledProperty<SeatLayoutSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<SeatingCanvas, SeatLayoutSnapshot?>(nameof(Snapshot));

    public static readonly StyledProperty<string?> SelectedSeatIdProperty =
        AvaloniaProperty.Register<SeatingCanvas, string?>(nameof(SelectedSeatId));

    static SeatingCanvas()
    {
        AffectsRender<SeatingCanvas>(SnapshotProperty, SelectedSeatIdProperty);
    }

    public SeatingCanvas()
    {
        ClipToBounds = true;
        // 键盘虚拟焦点（M1 补齐方向键导航）
        Focusable = true;
        ActualThemeVariantChanged += (_, _) =>
        {
            _palette = null;
            InvalidateVisual();
        };
    }

    /// <summary>座位布局快照（整体替换触发重绘）。</summary>
    public SeatLayoutSnapshot? Snapshot
    {
        get => GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    /// <summary>当前选中座位 ID（null 表示无选中）。</summary>
    public string? SelectedSeatId
    {
        get => GetValue(SelectedSeatIdProperty);
        set => SetValue(SelectedSeatIdProperty, value);
    }

    private static readonly Typeface SeatTypeface = new(FontFamily.Default);
    private Palette? _palette;

    protected override Size MeasureOverride(Size availableSize)
    {
        var snapshot = Snapshot;
        if (snapshot is null || snapshot.BoardWidth <= 0 || snapshot.BoardHeight <= 0)
            return default;

        return new Size(
            snapshot.BoardWidth + (BoardMargin * 2),
            snapshot.BoardHeight + (BoardMargin * 2));
    }

    public override void Render(DrawingContext context)
    {
        var palette = EnsurePalette();
        var bounds = new Rect(Bounds.Size);

        // 方格纸底 + 发丝格线
        context.FillRectangle(palette.CanvasBg, bounds);
        DrawGrid(context, bounds, palette);

        var snapshot = Snapshot;
        if (snapshot is null || snapshot.Seats.Count == 0)
            return;

        var origin = new Point(BoardMargin, BoardMargin);
        var boardRect = new Rect(origin, new Size(snapshot.BoardWidth, snapshot.BoardHeight));
        context.DrawRectangle(palette.BoardBg, palette.BoardPen, boardRect, 8, 8);

        foreach (var seat in snapshot.Seats)
            DrawSeat(context, origin, seat, palette);
    }

    private static void DrawGrid(DrawingContext context, Rect bounds, Palette palette)
    {
        const double step = 24;
        var maxX = bounds.Width;
        var maxY = bounds.Height;

        for (double x = step; x < maxX; x += step)
            context.DrawLine(palette.GridPen, new Point(x, 0), new Point(x, maxY));

        for (double y = step; y < maxY; y += step)
            context.DrawLine(palette.GridPen, new Point(0, y), new Point(maxX, y));
    }

    private void DrawSeat(DrawingContext context, Point origin, SeatVisual seat, Palette palette)
    {
        var rect = new Rect(origin.X + seat.X, origin.Y + seat.Y, seat.Width, seat.Height);
        var isSelected = string.Equals(seat.Id, SelectedSeatId, StringComparison.Ordinal);

        IBrush fill;
        IPen border;

        if (seat.IsDisabled)
        {
            fill = palette.SeatDisabledBg;
            border = palette.SeatDisabledPen;
        }
        else if (seat.IsOccupied)
        {
            fill = palette.SeatOccupiedBg;
            border = palette.SeatOccupiedPen;
        }
        else
        {
            fill = palette.SeatBg;
            border = palette.SeatPen;
        }

        context.DrawRectangle(fill, border, rect, 5, 5);

        if (seat.IsFixed)
        {
            var dot = new Point(rect.Right - 6, rect.Top + 6);
            context.DrawEllipse(palette.FixedDot, null, dot, 2.5, 2.5);
        }

        if (seat.IsOccupied && !string.IsNullOrEmpty(seat.Label))
        {
            var text = new FormattedText(
                seat.Label,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                SeatTypeface,
                10,
                palette.SeatLabelFg);
            text.MaxTextWidth = Math.Max(0, seat.Width - 4);
            text.MaxTextHeight = seat.Height;

            var textOrigin = new Point(
                rect.X + ((rect.Width - text.Width) / 2),
                rect.Y + ((rect.Height - text.Height) / 2));
            context.DrawText(text, textOrigin);
        }

        if (isSelected)
            context.DrawRectangle(null, palette.SelectedPen, rect.Inflate(1), 6, 6);
    }

    private Palette EnsurePalette()
        => _palette ??= new Palette(
            Brush("SfCanvasBgBrush", Brushes.Transparent),
            Brush("SfCanvasGridBrush", Brushes.Transparent),
            Brush("SfBoardBgBrush", Brushes.White),
            Brush("SfBoardBorderBrush", Brushes.Gray),
            Brush("SfSeatBgBrush", Brushes.White),
            Brush("SfSeatBorderBrush", Brushes.Gray),
            Brush("SfSeatOccupiedBrush", Brushes.LightBlue),
            Brush("SfSeatOccupiedBorderBrush", Brushes.SteelBlue),
            Brush("SfSeatOccupiedFgBrush", Brushes.DarkBlue),
            Brush("SfAccentBrush", Brushes.DodgerBlue),
            Brush("SfFixedBrush", Brushes.DodgerBlue),
            Brush("SfSurface2Brush", Brushes.Gainsboro));

    private IBrush Brush(string key, IBrush fallback)
        => this.TryFindResource(key, out var value) && value is IBrush brush ? brush : fallback;

    private sealed record Palette(
        IBrush CanvasBg,
        IBrush Grid,
        IBrush BoardBg,
        IBrush BoardBorder,
        IBrush SeatBg,
        IBrush SeatBorder,
        IBrush SeatOccupiedBg,
        IBrush SeatOccupiedBorder,
        IBrush SeatLabelFg,
        IBrush Accent,
        IBrush Fixed,
        IBrush Surface2)
    {
        public IPen GridPen { get; } = new Pen(Grid, 1);
        public IPen BoardPen { get; } = new Pen(BoardBorder, 1);
        public IPen SeatPen { get; } = new Pen(SeatBorder, 1);
        public IPen SeatOccupiedPen { get; } = new Pen(SeatOccupiedBorder, 1);
        public IPen SeatDisabledPen { get; } = new Pen(BoardBorder, 1, new DashStyle([3, 3], 0));
        public IBrush SeatDisabledBg { get; } = Surface2;
        public IBrush FixedDot { get; } = Fixed;
        public IPen SelectedPen { get; } = new Pen(Accent, 2);
    }
}
