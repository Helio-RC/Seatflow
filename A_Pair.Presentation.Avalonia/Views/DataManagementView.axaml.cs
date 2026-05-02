using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace A_Pair.Presentation.Avalonia.Views;

public partial class DataManagementView : UserControl
{
    private const double CompactThreshold = 650;

    public DataManagementView()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty && DataContext is ViewModels.DataManagementViewModel vm)
        {
            vm.IsCompact = Bounds.Width < CompactThreshold;
        }
    }
}
