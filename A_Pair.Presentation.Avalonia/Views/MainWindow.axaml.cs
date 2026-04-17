using Avalonia.Controls;

namespace A_Pair.Presentation.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // when shell provides a seating view model, we can bind its workspace to the canvas
            var seatCanvas = this.FindControl<Controls.SeatCanvas>("SeatCanvas");
            // Safe hookup: subscribe to DataContext changes and try to render seating when available
            this.DataContextChanged += (s, e) =>
            {
                if (DataContext is ViewModels.MainShellViewModel shell)
                {
                    if (shell.CurrentView is ViewModels.SeatingArrangementViewModel seatingVm)
                    {
                        // try to fetch current workspace and render seats
                        var facade = seatingVm.GetType().GetProperty("Facade")?.GetValue(seatingVm) as A_Pair.Application.Interfaces.IApplicationFacade;
                        if (facade != null)
                        {
                            seatingVm.GetType().GetMethod("RefreshSeatsAsync")?.Invoke(seatingVm, new object[] { seatCanvas });
                        }
                    }
                }
            };
        }
    }
}