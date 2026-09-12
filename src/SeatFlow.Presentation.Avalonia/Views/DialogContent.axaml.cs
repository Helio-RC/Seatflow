using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using IconEnum = FluentIcons.Common.Icon;

namespace SeatFlow.Presentation.Avalonia.Views;

/// <summary>
/// 通用对话框内容控件（桌面版宿主于 <see cref="DialogWindow"/> 模态窗口；
/// 浏览器版宿主于主窗口 overlay，见 WebDialogService）。
/// </summary>
internal partial class DialogContent : UserControl
{
    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<DialogContent, string>(nameof(Message));

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public DialogKind Kind { get; set; } = DialogKind.Info;

    public static readonly StyledProperty<string?> Button1TextProperty =
        AvaloniaProperty.Register<DialogContent, string?>(nameof(Button1Text));
    public static readonly StyledProperty<string?> Button2TextProperty =
        AvaloniaProperty.Register<DialogContent, string?>(nameof(Button2Text));
    public static readonly StyledProperty<string?> Button3TextProperty =
        AvaloniaProperty.Register<DialogContent, string?>(nameof(Button3Text));

    public string? Button1Text { get => GetValue(Button1TextProperty); set => SetValue(Button1TextProperty, value); }
    public string? Button2Text { get => GetValue(Button2TextProperty); set => SetValue(Button2TextProperty, value); }
    public string? Button3Text { get => GetValue(Button3TextProperty); set => SetValue(Button3TextProperty, value); }

    /// <summary>任意对话框标题（由宿主设置 Window.Title 或直接赋值）。</summary>
    public string? DialogTitle { get; set; }

    /// <summary>对话框结果：0=第一个按钮, 1=第二个按钮, 2=第三个按钮, null=取消/关闭。</summary>
    public int? DialogResult { get; private set; }

    /// <summary>
    /// 按钮点击完成回调（对话框关闭信号）。桌面用法：事件中 Window.Close；
    /// 浏览器用法：WebDialogService 完成 TCS。
    /// </summary>
    public event Action<DialogContent>? Completed;

    public DialogContent()
    {
        InitializeComponent();

        OkButton.Click += (_, _) => RaiseCompleted(0);
        CancelButton.Click += (_, _) => RaiseCompleted(2);
        ThirdButton.Click += (_, _) => RaiseCompleted(1);
    }

    /// <summary>
    /// 从 _DialogWindow 迁移的初始化逻辑：设置图标、标题、按钮。
    /// 被桌面 Window 与浏览器 overlay 宿主双方调用。
    /// </summary>
    public void OnContentAttached()
    {
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
        TitleBlock.Text = DialogTitle ?? string.Empty;
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
            CancelButton.IsVisible = !string.IsNullOrWhiteSpace(Button3Text);
        }
    }

    private void RaiseCompleted(int? result)
    {
        DialogResult = result;
        Completed?.Invoke(this);
    }

    private static Color GetThemeColor(string key)
    {
        if (global::Avalonia.Application.Current is { } app && app.FindResource(key) is Color c)
            return c;
        return Colors.Gray;
    }
}

/// <summary>对话框种类（见 DialogWindow/DialogContent）。</summary>
internal enum DialogKind { Error, Warning, Info, Confirm, MultiOption }
