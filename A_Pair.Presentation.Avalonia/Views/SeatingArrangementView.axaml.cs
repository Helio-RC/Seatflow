using System;
using A_Pair.Presentation.Avalonia.ViewModels;
using Avalonia.Controls;

namespace A_Pair.Presentation.Avalonia.Views
{
    public partial class SeatingArrangementView : UserControl
    {
        public SeatingArrangementView ()
        {
            InitializeComponent();

            this.DataContextChanged += async (s , e) =>
            {
                if (DataContext is SeatingArrangementViewModel vm)
                {
                    // attempt to hook canvas and refresh
                    try
                    {
                        var canvas = this.FindControl<Controls.SeatCanvas>("SeatCanvas");
                        if (canvas != null)
                        {
                            await vm.RefreshSeatsAsync(canvas);
                        }
                    }
                    catch (Exception)
                    {
                        // swallow - best effort UI hookup
                    }
                }
            };
        }
    }
}
