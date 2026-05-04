using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;

namespace A_Pair.Presentation.Avalonia.Views;

public partial class FreeformManagementView : UserControl
{
    private ViewModels.FreeformManagementViewModel? _vm;

    public FreeformManagementView ()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged (EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
            _vm.PropertyChanged -= OnSidebarWidthChanged;
        _vm = DataContext as ViewModels.FreeformManagementViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnSidebarWidthChanged;
            SyncSidebar(_vm.SidebarListWidth);
        }
    }

    private void OnSidebarWidthChanged (object? sender , PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.FreeformManagementViewModel.SidebarListWidth)
            && sender is ViewModels.FreeformManagementViewModel vm)
        {
            SyncSidebar(vm.SidebarListWidth);
        }
    }

    private void SyncSidebar (double width)
    {
        var grid = this.FindControl<Grid>("SidebarGrid");
        if (grid != null && grid.ColumnDefinitions.Count > 0)
            grid.ColumnDefinitions[0].Width = new GridLength(width);
    }

    protected override void OnPropertyChanged (AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty && DataContext is ViewModels.FreeformManagementViewModel vm)
        {
            vm.OnWindowWidthChanged(Bounds.Width);
        }
    }
}
