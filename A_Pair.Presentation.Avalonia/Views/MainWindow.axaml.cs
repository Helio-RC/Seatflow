using Avalonia;
using Avalonia.Controls;

namespace A_Pair.Presentation.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow ()
        {
            InitializeComponent();
        }

        protected override void OnPropertyChanged (AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == BoundsProperty && DataContext is ViewModels.MainShellViewModel vm)
                vm.OnWindowWidthChanged(Bounds.Width);
        }
    }
}
