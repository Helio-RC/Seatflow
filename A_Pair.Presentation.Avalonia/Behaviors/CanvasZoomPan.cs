using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace A_Pair.Presentation.Avalonia.Behaviors;

public static class CanvasZoomPan
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("Enabled",
            typeof(CanvasZoomPan), defaultValue: false);

    private static readonly AttachedProperty<ScaleTransform> ScaleProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, ScaleTransform>("Scale",
            typeof(CanvasZoomPan));

    private static readonly AttachedProperty<TranslateTransform> TranslateProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, TranslateTransform>("Translate",
            typeof(CanvasZoomPan));

    private static readonly AttachedProperty<bool> IsPanningProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsPanning",
            typeof(CanvasZoomPan));

    private static readonly AttachedProperty<Point> PanOriginProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, Point>("PanOrigin",
            typeof(CanvasZoomPan));

    private static readonly AttachedProperty<Point> OriginalTranslateProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, Point>("OriginalTranslate",
            typeof(CanvasZoomPan));

    public static void SetEnabled(ScrollViewer element, bool value)
    {
        bool old = element.GetValue(EnabledProperty);
        if (old == value) return;
        element.SetValue(EnabledProperty, value);

        if (value)
        {
            element.PointerWheelChanged += OnPointerWheel;
            element.PointerPressed += OnPointerPressed;
            element.PointerMoved += OnPointerMoved;
            element.PointerReleased += OnPointerReleased;
        }
        else
        {
            element.PointerWheelChanged -= OnPointerWheel;
            element.PointerPressed -= OnPointerPressed;
            element.PointerMoved -= OnPointerMoved;
            element.PointerReleased -= OnPointerReleased;
        }
    }

    private static void EnsureTransforms(ScrollViewer sv)
    {
        if (sv.GetValue(ScaleProperty) != null) return;

        if (sv.Content is not Visual content) return;

        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(translate);
        content.RenderTransform = group;

        sv.SetValue(ScaleProperty, scale);
        sv.SetValue(TranslateProperty, translate);
    }

    private static void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        e.Handled = true;
        EnsureTransforms(sv);
        var scale = sv.GetValue(ScaleProperty)!;
        var translate = sv.GetValue(TranslateProperty)!;

        double factor = e.Delta.Y > 0 ? 1.12 : 1 / 1.12;
        double newScale = Math.Clamp(scale.ScaleX * factor, 0.1, 5.0);

        // 以鼠标位置为中心缩放
        var mousePos = e.GetPosition(sv);
        double dx = mousePos.X - (mousePos.X - translate.X) * (newScale / scale.ScaleX);
        double dy = mousePos.Y - (mousePos.Y - translate.Y) * (newScale / scale.ScaleY);

        scale.ScaleX = newScale;
        scale.ScaleY = newScale;
        translate.X = dx;
        translate.Y = dy;
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (!e.GetCurrentPoint(sv).Properties.IsMiddleButtonPressed) return;

        e.Handled = true;
        EnsureTransforms(sv);
        sv.SetValue(IsPanningProperty, true);
        sv.SetValue(PanOriginProperty, e.GetPosition(sv));
        sv.SetValue(OriginalTranslateProperty,
            new Point(sv.GetValue(TranslateProperty)!.X, sv.GetValue(TranslateProperty)!.Y));
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (!sv.GetValue(IsPanningProperty)) return;

        var origin = sv.GetValue(PanOriginProperty);
        var origTrans = sv.GetValue(OriginalTranslateProperty);
        var pos = e.GetPosition(sv);
        var translate = sv.GetValue(TranslateProperty)!;

        translate.X = origTrans.X + (pos.X - origin.X);
        translate.Y = origTrans.Y + (pos.Y - origin.Y);
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        sv.SetValue(IsPanningProperty, false);
    }
}
