using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SeatFlow.Presentation.Avalonia.Views;

/// <summary>
/// 单行文本输入内容控件（桌面版宿主于 <see cref="InputWindow"/>；
/// 浏览器版宿主于主窗口 overlay）。
/// </summary>
internal partial class InputContent : UserControl
{
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

    /// <summary>完成回调：true=确认，false=取消。</summary>
    public event Action<InputContent , bool>? Completed;

    public InputContent ()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Completed?.Invoke(this , true);
        CancelButton.Click += (_, _) => Completed?.Invoke(this , false);
    }

    /// <summary>聚焦输入框（overlay 宿主附加后调用）。</summary>
    public void OnContentAttached ()
    {
        InputBox.Focus();
        InputBox.SelectAll();
    }
}
