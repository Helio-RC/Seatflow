using Avalonia.Controls;
using Avalonia.Interactivity;

namespace A_Pair.Presentation.Avalonia.Views;

internal partial class InputWindow : Window
{
    private bool _result;

    public string Prompt
    {
        get => PromptBlock.Text ?? "";
        set => PromptBlock.Text = value;
    }

    public string Input
    {
        get => InputBox.Text ?? "";
        set => InputBox.Text = value;
    }

    public InputWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        InputBox.Focus();
        InputBox.SelectAll();

        OkButton.Click += (_, _) => { _result = true; Close(_result); };
        CancelButton.Click += (_, _) => { _result = false; Close(_result); };
    }
}
