using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using IconEnum = FluentIcons.Common.Icon;

namespace A_Pair.Presentation.Avalonia.Views;

internal partial class DialogWindow : Window
{
    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<DialogWindow, string>(nameof(Message));

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public DialogKind Kind { get; set; } = DialogKind.Info;

    public DialogWindow ()
    {
        InitializeComponent();

        OkButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    protected override void OnLoaded (RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var (icon, color) = Kind switch
        {
            DialogKind.Error => (IconEnum.ErrorCircle, GetThemeColor("ColorError")),
            DialogKind.Warning => (IconEnum.Warning, GetThemeColor("ColorWarning")),
            DialogKind.Info => (IconEnum.Info, GetThemeColor("ColorInfo")),
            DialogKind.Confirm => (IconEnum.QuestionCircle, GetThemeColor("ColorInfo")),
            _ => (IconEnum.Info, GetThemeColor("ColorInfo"))
        };

        DialogIcon.Icon = icon;
        DialogIcon.Foreground = new SolidColorBrush(color);
        TitleBlock.Text = Title ?? string.Empty;
        MessageBlock.Text = Message ?? string.Empty;

        if (Kind == DialogKind.Confirm)
        {
            OkButton.Content = "确定";
            CancelButton.IsVisible = true;
        }
    }

    private Color GetThemeColor (string key)
    {
        if (global::Avalonia.Application.Current is { } app && app.FindResource(key) is Color c)
            return c;
        return Colors.Gray;
    }
}

internal enum DialogKind { Error, Warning, Info, Confirm }
