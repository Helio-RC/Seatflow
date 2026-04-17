using Avalonia.Controls;
using Avalonia.Input;
using System.Collections.Generic;
using System.Linq;
using A_Pair.Core.Models;
using Avalonia;
using Avalonia.Media;
using System;
using Avalonia.Controls.Shapes;
using Avalonia.Collections;
using Avalonia.VisualTree;
using System.Collections.Concurrent;

namespace A_Pair.Presentation.Avalonia.Controls
{
    public partial class SeatCanvas : UserControl
    {
        private Canvas _canvas;
        private double _scale = 1.0;
        private bool _isPanning = false;
        private Point _panStart;
        private TranslateTransform _translate = new TranslateTransform();
        private ScaleTransform _scaler = new ScaleTransform();

        // quick lookup for rectangles by seat id
        private readonly ConcurrentDictionary<string, Shape> _seatShapes = new();

        public event Action<string, string>? OnSeatAssigned;
        public event Action<string>? OnSeatSelected;
        public event Action<string>? OnSeatRightClicked;

        public SeatCanvas()
        {
            this.InitializeComponent();
            _canvas = this.FindControl<Canvas>("PART_Canvas");
            // compose transforms for zoom/pan
            var group = new TransformGroup();
            group.Children.Add(_scaler);
            group.Children.Add(_translate);
            _canvas.RenderTransform = group;

            // pointer wheel zoom
            this.PointerWheelChanged += SeatCanvas_PointerWheelChanged;
            // middle mouse pan
            this.PointerPressed += SeatCanvas_PointerPressed;
            this.PointerMoved += SeatCanvas_PointerMoved;
            this.PointerReleased += SeatCanvas_PointerReleasedGlobal;
        }

        public void RenderSeats(IEnumerable<Seat> seats)
        {
            _canvas.Children.Clear();
            _seatShapes.Clear();
            foreach (var seat in seats)
            {
                var rect = new Rectangle()
                {
                    Width = 30,
                    Height = 30,
                    Fill = Brushes.LightGray,
                    Stroke = Brushes.Black
                };

                double x = 0, y = 0;
                if (seat is GridSeat gs)
                {
                    x = (gs.Column - 1) * 40;
                    y = (gs.Row - 1) * 40;
                }
                else if (seat is PolarSeat ps)
                {
                    // simple polar placement
                    var rad = ps.Radius;
                    var ang = ps.AngleDegrees * Math.PI / 180.0;
                    x = 200 + rad * System.Math.Cos(ang);
                    y = 200 + rad * System.Math.Sin(ang);
                }

                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                // attach seat id to Tag for interaction
                rect.Tag = seat.Id;
                rect.PointerPressed += OnRectPointerPressed;
                rect.PointerReleased += OnRectPointerReleased;
                rect.ContextMenu = null; // leave for runtime if needed
                _canvas.Children.Add(rect);
                _seatShapes[seat.Id] = rect;
            }
        }

        // render presentation seats (used by Venue preview)
        public void RenderPresentationSeats(IEnumerable<PresentationSeat> seats)
        {
            _canvas.Children.Clear();
            _seatShapes.Clear();
            foreach (var seat in seats)
            {
                var rect = new Rectangle()
                {
                    Width = 30,
                    Height = 30,
                    Fill = Brushes.LightGray,
                    Stroke = Brushes.Black
                };

                double x = 0, y = 0;
                if (seat.Row.HasValue && seat.Column.HasValue)
                {
                    x = (seat.Column.Value - 1) * 40;
                    y = (seat.Row.Value - 1) * 40;
                }
                else if (seat.Radius.HasValue && seat.AngleDegrees.HasValue)
                {
                    var rad = seat.Radius.Value;
                    var ang = seat.AngleDegrees.Value * Math.PI / 180.0;
                    x = 200 + rad * Math.Cos(ang);
                    y = 200 + rad * Math.Sin(ang);
                }

                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                rect.Tag = seat.Id;
                rect.PointerPressed += OnRectPointerPressed;
                rect.PointerReleased += OnRectPointerReleased;
                _canvas.Children.Add(rect);
                _seatShapes[seat.Id] = rect;
            }
        }

        private string? _pressedSeatId;
        private void OnRectPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Shape s && s.Tag is string id)
            {
                _pressedSeatId = id;
                // left click selects
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    SelectSeat(id);
                }
                // right click
                if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
                {
                    OnSeatRightClicked?.Invoke(id);
                }
            }
        }

        private void OnRectPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (sender is Shape s && s.Tag is string releasedId && _pressedSeatId != null)
            {
                // simple assign: notify with pressed->released as fromSeat->toSeat (UI can interpret as assign)
                OnSeatAssigned?.Invoke(releasedId, _pressedSeatId);
            }
            _pressedSeatId = null;
        }

        private void SelectSeat(string id)
        {
            // highlight selected and raise event
            foreach (var kv in _seatShapes)
            {
                if (kv.Value is Shape sh)
                {
                    sh.Fill = Brushes.LightGray;
                }
            }

            if (_seatShapes.TryGetValue(id, out var shape))
            {
                shape.Fill = Brushes.LightBlue;
            }

            OnSeatSelected?.Invoke(id);
        }

        private void SeatCanvas_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            var delta = e.Delta.Y > 0 ? 0.1 : -0.1;
            SetScale(_scale + delta);
        }

        public void ZoomIn() => SetScale(_scale + 0.1);
        public void ZoomOut() => SetScale(Math.Max(0.1, _scale - 0.1));

        private void SetScale(double scale)
        {
            _scale = Math.Max(0.1, Math.Min(4.0, scale));
            _scaler.ScaleX = _scale;
            _scaler.ScaleY = _scale;
        }

        private void SeatCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsMiddleButtonPressed)
            {
                _isPanning = true;
                _panStart = e.GetPosition(this);
                this.Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand);
            }
        }

        private void SeatCanvas_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isPanning)
            {
                var pos = e.GetPosition(this);
                var dx = pos.X - _panStart.X;
                var dy = pos.Y - _panStart.Y;
                _translate.X += dx;
                _translate.Y += dy;
                _panStart = pos;
            }
        }

        private void SeatCanvas_PointerReleasedGlobal(object? sender, PointerReleasedEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                this.Cursor = new Avalonia.Input.Cursor(StandardCursorType.Arrow);
            }
        }
    }
}
