using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace SeatFlow.Presentation.Avalonia.Controls;

/// <summary>
/// 支持长按语义的按钮（排座工作台「长按连续生成」的输入基础）：
/// - 指针按住超过 <see cref="HoldDelay"/> 后触发一次 <see cref="HoldStarted"/>，并抑制松开时的 Click；
/// - 松开指针 / 指针捕获丢失 / 控件卸载 / 被禁用时触发 <see cref="HoldEnded"/>（未进入长按时不触发）；
/// - 短按（阈值内松开）行为与普通按钮完全一致（松开触发 Click / Command）；
/// - 仅响应指针按下，键盘不参与长按判定（键盘单击仍走基类行为）。
/// 本控件不承担重复节奏：连续触发由持有方在 <see cref="HoldStarted"/> 之后自行循环。
/// </summary>
public class HoldRepeatButton : Button
{
    /// <summary>长按判定阈值（毫秒）。</summary>
    public static readonly StyledProperty<int> HoldDelayProperty =
        AvaloniaProperty.Register<HoldRepeatButton, int>(nameof(HoldDelay), defaultValue: 400);

    private DispatcherTimer? _holdTimer;
    private bool _holdTriggered;
    private int? _holdPointerId;

    /// <summary>长按激活（按住达到阈值）时触发。</summary>
    public event EventHandler? HoldStarted;

    /// <summary>长按结束（松开 / 捕获丢失 / 卸载 / 禁用）时触发。</summary>
    public event EventHandler? HoldEnded;

    public int HoldDelay
    {
        get => GetValue(HoldDelayProperty);
        set => SetValue(HoldDelayProperty, value);
    }

    /// <summary>复用 <see cref="Button"/> 的默认控件主题（派生控件需显式指向基类 StyleKey）。</summary>
    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // 已有指针主导本次长按判定（如触摸多点）：忽略其余按下，
        // 避免重置长按状态导致松开时不再触发 HoldEnded、会话失去停止信号
        if (_holdPointerId != null) return;

        _holdPointerId = e.Pointer.Id;
        _holdTriggered = false;
        StartHoldTimer();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        // 基类在 ClickMode=Release 且指针位于按钮上时会调用 OnClick；
        // 此时 _holdTriggered 仍为 true，OnClick 被抑制（避免长按结束后又执行一次单击命令）
        base.OnPointerReleased(e);

        if (e.Pointer.Id != _holdPointerId) return;
        EndHold();
    }

    protected override void OnClick()
    {
        if (_holdTriggered) return;
        base.OnClick();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        if (e.Pointer.Id != _holdPointerId) return;
        EndHold();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        EndHold();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // 按住期间被禁用（例如命令 CanExecute 变化）时兜底结束长按，避免会话失去停止信号
        if (change.Property == IsEffectivelyEnabledProperty && change.GetNewValue<bool>() == false)
            EndHold();
    }

    private void StartHoldTimer()
    {
        if (_holdTimer == null)
        {
            _holdTimer = new DispatcherTimer();
            _holdTimer.Tick += OnHoldTick;
        }

        _holdTimer.Stop();
        _holdTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, HoldDelay));
        _holdTimer.Start();
    }

    private void OnHoldTick(object? sender, EventArgs e)
    {
        _holdTimer?.Stop();
        _holdTriggered = true;
        HoldStarted?.Invoke(this, EventArgs.Empty);
    }

    private void StopHoldTimer() => _holdTimer?.Stop();

    /// <summary>结束本次长按（松开 / 捕获丢失 / 卸载 / 禁用）：停表并按需触发 HoldEnded。</summary>
    private void EndHold()
    {
        StopHoldTimer();
        _holdPointerId = null;
        if (!_holdTriggered) return;

        _holdTriggered = false;
        HoldEnded?.Invoke(this, EventArgs.Empty);
    }
}
