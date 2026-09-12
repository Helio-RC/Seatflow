using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SeatFlow.Presentation.Avalonia.Views;

/// <summary>
/// 通用对话框宿主窗口（桌面版）。内容为 <see cref="DialogContent"/>；
/// 浏览器版不使用本类（无独立窗口），改由主窗口 overlay 承载。
/// </summary>
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

    public static readonly StyledProperty<string?> Button1TextProperty =
        AvaloniaProperty.Register<DialogWindow, string?>(nameof(Button1Text));
    public static readonly StyledProperty<string?> Button2TextProperty =
        AvaloniaProperty.Register<DialogWindow, string?>(nameof(Button2Text));
    public static readonly StyledProperty<string?> Button3TextProperty =
        AvaloniaProperty.Register<DialogWindow, string?>(nameof(Button3Text));

    public string? Button1Text { get => GetValue(Button1TextProperty); set => SetValue(Button1TextProperty, value); }
    public string? Button2Text { get => GetValue(Button2TextProperty); set => SetValue(Button2TextProperty, value); }
    public string? Button3Text { get => GetValue(Button3TextProperty); set => SetValue(Button3TextProperty, value); }

    public int? DialogResult { get; private set; }

    public DialogWindow()
    {
        InitializeComponent();
        ContentRoot.Completed += OnContentCompleted;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (ContentRoot is { } content)
        {
            content.DialogTitle = Title ?? string.Empty;
            content.Message = Message ?? string.Empty;
            content.Kind = Kind;
            content.Button1Text = Button1Text;
            content.Button2Text = Button2Text;
            content.Button3Text = Button3Text;
            content.OnContentAttached();
        }
    }

    private void OnContentCompleted(DialogContent content)
    {
        DialogResult = content.DialogResult;
        // 0（OK/主按钮）与 1（第三按钮）视为确认 → true；2（取消）→ false。
        // 推迟 Close 到当前消息处理完成后执行，避免按钮 Click 回调内直接
        // Close → WindowImpl.Dispose 嵌套消息处理导致 WinUI compositor Monitor 重入死锁。
        var confirmed = content.DialogResult is 0 or 1;
        Dispatcher.UIThread.Post(() => Close(confirmed));
    }
}
