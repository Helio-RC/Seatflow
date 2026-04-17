using Avalonia.Controls;

namespace A_Pair.Presentation.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            if (DataContext is ViewModels.MainWindowViewModel vm)
            {
                var seatCanvas = this.FindControl<Controls.SeatCanvas>("SeatCanvas");
                seatCanvas?.RenderSeats(vm.Seats);
            }
        }
    }
}