using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace A_Pair.Presentation.Avalonia.Views;

public partial class SeatingArrangementView : UserControl
{
    public SeatingArrangementView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SeatingArrangementViewModel vm)
            _ = vm.RefreshDataAsync();
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
