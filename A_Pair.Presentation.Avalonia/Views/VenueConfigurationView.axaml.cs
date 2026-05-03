using Avalonia;
using Avalonia.Controls;

namespace A_Pair.Presentation.Avalonia.Views
{
    public partial class VenueConfigurationView : UserControl
    {
        public VenueConfigurationView ()
        {
            InitializeComponent();
        }

        protected override void OnPropertyChanged (AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == BoundsProperty && DataContext is ViewModels.VenueConfigurationViewModel vm)
            {
                vm.OnWindowWidthChanged(Bounds.Width);
            }
        }
    }
}
