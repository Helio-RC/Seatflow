using Avalonia;
using Avalonia.Controls;

namespace A_Pair.Presentation.Avalonia.Views;

public partial class DataManagementView : UserControl
{
    private const double CompactThreshold = 800;

    public DataManagementView ()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged (AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty && DataContext is ViewModels.DataManagementViewModel vm)
        {
            vm.IsCompact = Bounds.Width < CompactThreshold;
        }
    }
}
