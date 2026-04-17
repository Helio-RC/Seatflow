using Avalonia.Controls;
using Avalonia.Input;
using System.Collections.Generic;
using System.Linq;
using A_Pair.Core.Models;
using Avalonia;
using Avalonia.Media;
using System;
using Avalonia.Controls.Shapes;

namespace A_Pair.Presentation.Avalonia.Controls
{
    public partial class SeatCanvas : UserControl
    {
        private Canvas _canvas;

        public SeatCanvas()
        {
            this.InitializeComponent();
            _canvas = this.FindControl<Canvas>("PART_Canvas");
        }

        public void RenderSeats(IEnumerable<Seat> seats)
        {
            _canvas.Children.Clear();
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
                _canvas.Children.Add(rect);
            }
        }
    }
}
