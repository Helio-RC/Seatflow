using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SeatFlow.Presentation.Avalonia.Views;

internal partial class InputWindow : Window
{
    public string Prompt
    {
        get => ContentRoot.Prompt;
        set => ContentRoot.Prompt = value;
    }

    public string Input
    {
        get => ContentRoot.Input;
        set => ContentRoot.Input = value;
    }

    public InputWindow()
    {
        InitializeComponent();
        ContentRoot.Completed += (_, confirmed) =>
        {
            Input = ContentRoot.Input;
            Close(confirmed);
        };
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ContentRoot.OnContentAttached();
    }
}
