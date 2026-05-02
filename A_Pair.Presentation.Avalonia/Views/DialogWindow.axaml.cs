using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using FluentIcons.Avalonia;
using IconEnum = FluentIcons.Common.Icon;

namespace A_Pair.Presentation.Avalonia.Views;

internal partial class DialogWindow : Window
{
    private bool _result;

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<DialogWindow, string>(nameof(Message));

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public DialogKind Kind { get; set; } = DialogKind.Info;

    public DialogWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var (icon, color) = Kind switch
        {
            DialogKind.Error => (IconEnum.ErrorCircle, Color.Parse("#DC2626")),
            DialogKind.Warning => (IconEnum.Warning, Color.Parse("#F59E0B")),
            DialogKind.Info => (IconEnum.Info, Color.Parse("#2563EB")),
            DialogKind.Confirm => (IconEnum.QuestionCircle, Color.Parse("#2563EB")),
            _ => (IconEnum.Info, Color.Parse("#2563EB"))
        };

        DialogIcon.Icon = icon;
        DialogIcon.Foreground = new SolidColorBrush(color);
        TitleBlock.Text = Title ?? string.Empty;
        MessageBlock.Text = Message ?? string.Empty;

        OkButton.Click += (_, _) => { _result = true; Close(_result); };
        CancelButton.Click += (_, _) => { _result = false; Close(_result); };

        if (Kind == DialogKind.Confirm)
        {
            OkButton.Content = "确定";
            CancelButton.IsVisible = true;
        }
    }
}

internal enum DialogKind { Error, Warning, Info, Confirm }
