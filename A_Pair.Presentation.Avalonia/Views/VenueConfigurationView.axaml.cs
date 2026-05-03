using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;

namespace A_Pair.Presentation.Avalonia.Views;

public partial class VenueConfigurationView : UserControl
{
    private ViewModels.VenueConfigurationViewModel? _vm;

    public VenueConfigurationView ()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged (EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
            _vm.PropertyChanged -= OnSidebarWidthChanged;
        _vm = DataContext as ViewModels.VenueConfigurationViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnSidebarWidthChanged;
            SyncSidebar(_vm.SidebarListWidth);
        }
    }

    private void OnSidebarWidthChanged (object? sender , PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.VenueConfigurationViewModel.SidebarListWidth)
            && sender is ViewModels.VenueConfigurationViewModel vm)
        {
            SyncSidebar(vm.SidebarListWidth);
        }
    }

    private void SyncSidebar (double width)
    {
        var root = Content as DockPanel;
        if (root == null) return;
        foreach (var child in root.Children)
        {
            if (child is Grid grid && grid.ColumnDefinitions.Count >= 3)
            {
                grid.ColumnDefinitions[0].Width = new GridLength(width);
                break;
            }
        }
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
