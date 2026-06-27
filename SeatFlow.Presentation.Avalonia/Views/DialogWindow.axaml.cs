using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using IconEnum = FluentIcons.Common.Icon;

namespace A_Pair.Presentation.Avalonia.Views;

internal partial class DialogWindow : Window
{
    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<DialogWindow , string>(nameof(Message));

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty , value);
    }

    public DialogKind Kind { get; set; } = DialogKind.Info;

    public static readonly StyledProperty<string?> Button1TextProperty =
        AvaloniaProperty.Register<DialogWindow , string?>(nameof(Button1Text));
    public static readonly StyledProperty<string?> Button2TextProperty =
        AvaloniaProperty.Register<DialogWindow , string?>(nameof(Button2Text));
    public static readonly StyledProperty<string?> Button3TextProperty =
        AvaloniaProperty.Register<DialogWindow , string?>(nameof(Button3Text));

    public string? Button1Text { get => GetValue(Button1TextProperty); set => SetValue(Button1TextProperty , value); }
    public string? Button2Text { get => GetValue(Button2TextProperty); set => SetValue(Button2TextProperty , value); }
    public string? Button3Text { get => GetValue(Button3TextProperty); set => SetValue(Button3TextProperty , value); }

    public int? DialogResult { get; private set; }

    public DialogWindow ()
    {
        InitializeComponent();

        OkButton.Click += (_ , _) => { DialogResult = 0; Close(true); };
        CancelButton.Click += (_ , _) => { DialogResult = 2; Close(false); };
        ThirdButton.Click += (_ , _) => { DialogResult = 1; Close(true); };
    }

    protected override void OnLoaded (RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var (icon , color) = Kind switch
        {
            DialogKind.Error => (IconEnum.ErrorCircle , GetThemeColor("ColorError")),
            DialogKind.Warning => (IconEnum.Warning , GetThemeColor("ColorWarning")),
            DialogKind.Info => (IconEnum.Info , GetThemeColor("ColorInfo")),
            DialogKind.Confirm => (IconEnum.QuestionCircle , GetThemeColor("ColorInfo")),
            _ => (IconEnum.Info , GetThemeColor("ColorInfo"))
        };

        DialogIcon.Icon = icon;
        DialogIcon.Foreground = new SolidColorBrush(color);
        TitleBlock.Text = Title ?? string.Empty;
        MessageBlock.Text = Message ?? string.Empty;

        if (Kind == DialogKind.Confirm)
        {
            CancelButton.IsVisible = true;
        }
        else if (Kind == DialogKind.MultiOption)
        {
            OkButton.Content = Button1Text ?? Lang.Resources.Common_OK;
            ThirdButton.Content = Button2Text ?? Lang.Resources.Common_OK;
            CancelButton.Content = Button3Text ?? Lang.Resources.Common_Cancel;
            ThirdButton.IsVisible = true;
            CancelButton.IsVisible = true;
        }
    }

    private static Color GetThemeColor (string key)
    {
        if (global::Avalonia.Application.Current is { } app && app.FindResource(key) is Color c)
            return c;
        return Colors.Gray;
    }
}

internal enum DialogKind { Error, Warning, Info, Confirm, MultiOption }
