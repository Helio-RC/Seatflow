using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

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
        {
            vm.CapturePreviewAsync = CapturePreviewAsync;
            _ = vm.RefreshDataAsync();
        }
    }

    private async Task<string?> CapturePreviewAsync()
    {
        var target = PreviewScrollViewer;
        if (target == null) return null;

        var pixelSize = new PixelSize(
            (int)Math.Ceiling(target.Bounds.Width),
            (int)Math.Ceiling(target.Bounds.Height));
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0) return null;

        var renderTarget = new RenderTargetBitmap(pixelSize);
        renderTarget.Render(target);

        var tempPath = System.IO.Path.GetTempFileName() + ".png";
        renderTarget.Save(tempPath);
        return tempPath;
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
