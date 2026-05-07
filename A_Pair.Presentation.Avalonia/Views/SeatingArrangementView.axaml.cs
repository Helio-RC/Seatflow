using Avalonia.Controls;
using Avalonia.Input;

namespace A_Pair.Presentation.Avalonia.Views;

public partial class SeatingArrangementView : UserControl
{
    public SeatingArrangementView()
    {
        InitializeComponent();
    }

    private void SeatBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border
            && border.DataContext is ViewModels.SeatDisplayItem item
            && DataContext is ViewModels.SeatingArrangementViewModel vm)
        {
            vm.ClickSeatCommand.Execute(item);
        }
    }
}
