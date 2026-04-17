namespace A_Pair.Presentation.Avalonia.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public string Greeting { get; } = "Welcome to Avalonia!";

        public System.Collections.Generic.List<A_Pair.Core.Models.Seat> Seats { get; } = new()
        {
            new A_Pair.Core.Models.GridSeat { Row = 1, Column = 1 },
            new A_Pair.Core.Models.GridSeat { Row = 1, Column = 2 },
            new A_Pair.Core.Models.GridSeat { Row = 2, Column = 1 },
            new A_Pair.Core.Models.PolarSeat { Radius = 50, AngleDegrees = 0 }
        };
    }
}
