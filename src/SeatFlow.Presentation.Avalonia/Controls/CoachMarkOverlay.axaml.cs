using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Controls;

/// <summary>
/// 交接卡遮罩：自绘目标高亮 + 非模态卡片。只有卡片可命中，目标按钮保持可点击。
/// </summary>
public partial class CoachMarkOverlay : UserControl
{
    private const double CardWidth = 356;
    private const double Edge = 14;

    private MainShellViewModel? _shell;
    private Control? _target;
    private OobeViewModel? _oobe;

    public CoachMarkOverlay()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
        DataContextChanged += OnDataContextChanged;
        LayoutUpdated += (_, _) => UpdatePosition();
    }

    /// <summary>高亮目标（由 MainView 在视觉树中解析）。</summary>
    public Control? Target => _target;

    public void SetTarget(Control? target)
    {
        if (ReferenceEquals(_target, target)) return;

        if (_target is not null)
            _target.LayoutUpdated -= OnTargetLayoutUpdated;

        _target = target;
        if (_target is not null)
            _target.LayoutUpdated += OnTargetLayoutUpdated;

        UpdatePosition();
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachShell(DataContext as MainShellViewModel);
        UpdatePosition();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachShell(null);
        SetTarget(null);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachShell(DataContext as MainShellViewModel);
        UpdatePosition();
    }

    private void AttachShell(MainShellViewModel? shell)
    {
        if (ReferenceEquals(_shell, shell)) return;

        if (_oobe is not null)
            _oobe.PropertyChanged -= OnOobePropertyChanged;

        _shell = shell;
        _oobe = shell?.Oobe;

        if (_oobe is not null)
            _oobe.PropertyChanged += OnOobePropertyChanged;
    }

    private void OnOobePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OobeViewModel.IsHandoffVisible)
            or nameof(OobeViewModel.HandoffState)
            or nameof(OobeViewModel.HandoffTitle)
            or nameof(OobeViewModel.HandoffMessage))
        {
            UpdatePosition();
        }
    }

    private void OnTargetLayoutUpdated(object? sender, EventArgs e) => UpdatePosition();

    private void UpdatePosition()
    {
        if (_shell is null || _oobe is null || !_oobe.IsHandoffVisible || Bounds.Width <= 0)
            return;

        Card.Width = Math.Min(CardWidth, Math.Max(240, Bounds.Width - (Edge * 2)));
        Card.Measure(new Size(Card.Width, double.PositiveInfinity));
        var cardSize = Card.DesiredSize;
        var state = _oobe.HandoffState;

        CardIcon.Icon = state switch
        {
            OobeHandoffState.Success => Icon.CheckmarkCircle,
            OobeHandoffState.Failure => Icon.Dismiss,
            _ => Icon.Info,
        };

        if (state is OobeHandoffState.Success or OobeHandoffState.Incomplete)
        {
            Mask.IsVisible = state == OobeHandoffState.Incomplete;
            Mask.Hole = null;
            Mask.InvalidateVisual();
            CenterCard(cardSize);
            return;
        }

        var rect = GetTargetRect();
        if (rect is null)
        {
            Mask.IsVisible = true;
            Mask.Hole = null;
            Mask.InvalidateVisual();
            CenterCard(cardSize);
            return;
        }

        var hole = new Rect(rect.Value.X - 5, rect.Value.Y - 5, rect.Value.Width + 10, rect.Value.Height + 10);
        Mask.IsVisible = true;
        Mask.Hole = hole;
        Mask.InvalidateVisual();

        if (_shell.Layout.IsPhone || Bounds.Width <= 640)
        {
            PlaceCard(Edge, Math.Max(Edge, Bounds.Height - cardSize.Height - 16), cardSize);
            return;
        }

        double left = rect.Value.Center.X - (cardSize.Width / 2);
        double top = rect.Value.Bottom + 14;
        if (top + cardSize.Height > Bounds.Height - Edge)
            top = rect.Value.Top - cardSize.Height - 14;

        PlaceCard(
            Math.Clamp(left, Edge, Math.Max(Edge, Bounds.Width - cardSize.Width - Edge)),
            Math.Clamp(top, Edge, Math.Max(Edge, Bounds.Height - cardSize.Height - Edge)),
            cardSize);
    }

    private Rect? GetTargetRect()
    {
        if (_target is null || !_target.IsEffectivelyVisible) return null;
        var point = _target.TranslatePoint(new Point(0, 0), this);
        if (point is null) return null;

        var rect = new Rect(point.Value, _target.Bounds.Size);
        if (rect.Width <= 0 || rect.Height <= 0) return null;
        return rect;
    }

    private void CenterCard(Size cardSize)
    {
        PlaceCard(
            Math.Max(Edge, (Bounds.Width - cardSize.Width) / 2),
            Math.Max(Edge, (Bounds.Height - cardSize.Height) / 2),
            cardSize);
    }

    private void PlaceCard(double left, double top, Size cardSize)
    {
        Canvas.SetLeft(Card, left);
        Canvas.SetTop(Card, top);
    }
}

/// <summary>交接遮罩绘制：整屏暗色 + 目标挖空 + 目标描边。</summary>
public sealed class CoachMask : Control
{
    public static readonly StyledProperty<IBrush?> MaskBrushProperty =
        AvaloniaProperty.Register<CoachMask, IBrush?>(nameof(MaskBrush));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<CoachMask, IBrush?>(nameof(AccentBrush));

    public IBrush? MaskBrush
    {
        get => GetValue(MaskBrushProperty);
        set => SetValue(MaskBrushProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Rect? Hole { get; set; }

    static CoachMask()
    {
        AffectsRender<CoachMask>(MaskBrushProperty, AccentBrushProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var brush = MaskBrush ?? new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        if (Hole is not { } hole || hole.Width <= 0 || hole.Height <= 0)
        {
            context.FillRectangle(brush, new Rect(0, 0, width, height));
            return;
        }

        var top = Math.Max(0, hole.Top);
        var bottom = Math.Min(height, hole.Bottom);
        var left = Math.Max(0, hole.Left);
        var right = Math.Min(width, hole.Right);

        context.FillRectangle(brush, new Rect(0, 0, width, top));
        context.FillRectangle(brush, new Rect(0, bottom, width, Math.Max(0, height - bottom)));
        context.FillRectangle(brush, new Rect(0, top, left, Math.Max(0, bottom - top)));
        context.FillRectangle(brush, new Rect(right, top, Math.Max(0, width - right), Math.Max(0, bottom - top)));

        if (AccentBrush is not null)
        {
            context.DrawRectangle(
                null,
                new Pen(AccentBrush, 3),
                new Rect(hole.X, hole.Y, hole.Width, hole.Height),
                6,
                6);
        }
    }
}
