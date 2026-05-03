using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;

namespace A_Pair.Presentation.Avalonia.Views;

public partial class DataManagementView : UserControl
{
    private ViewModels.DataManagementViewModel? _vm;

    public DataManagementView ()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged (EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
            _vm.PropertyChanged -= OnSidebarWidthChanged;
        _vm = DataContext as ViewModels.DataManagementViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnSidebarWidthChanged;
            SyncSidebar(_vm.SidebarListWidth);
        }
    }

    private void OnSidebarWidthChanged (object? sender , PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.DataManagementViewModel.SidebarListWidth)
            && sender is ViewModels.DataManagementViewModel vm)
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

        if (change.Property == BoundsProperty && DataContext is ViewModels.DataManagementViewModel vm)
        {
            vm.OnWindowWidthChanged(Bounds.Width);
        }
    }
}
