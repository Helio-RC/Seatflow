using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using FluentAssertions;
using SeatFlow.Presentation.Avalonia.Controls;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 长按按钮 <see cref="HoldRepeatButton"/>：
/// 阈值内松开 = 普通单击；越过阈值触发 HoldStarted 且松开不再触发 Click；
/// 捕获丢失 / 被禁用 / 卸载时兜底结束长按（只触发一次 HoldEnded）。
/// </summary>
public class HoldRepeatButtonTests
{
    private const int HoldDelayMs = 50;

    private static (HoldRepeatButton Button, Window Window) CreateButtonWindow()
    {
        var button = new HoldRepeatButton { HoldDelay = HoldDelayMs, Width = 120, Height = 40 };
        var window = new Window { Width = 200, Height = 100, Content = button };
        window.Show();
        window.UpdateLayout();
        return (button, window);
    }

    private static Point Center(Window window, HoldRepeatButton button)
        => button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var started = Environment.TickCount64;
        while (!condition() && Environment.TickCount64 - started < timeoutMs)
            await Task.Delay(15);
    }

    [AvaloniaFact]
    public void 短按_触发一次点击_不进入长按()
    {
        var (button, window) = CreateButtonWindow();
        var clicks = 0;
        var startedCount = 0;
        var endedCount = 0;
        button.Click += (_, _) => clicks++;
        button.HoldStarted += (_, _) => startedCount++;
        button.HoldEnded += (_, _) => endedCount++;

        var point = Center(window, button);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        clicks.Should().Be(1, "阈值内松开应保持普通按钮的单击行为");
        startedCount.Should().Be(0);
        endedCount.Should().Be(0);
    }

    [AvaloniaFact]
    public async Task 长按_进入会话_松开不再点击_并可再次短按()
    {
        var (button, window) = CreateButtonWindow();
        var clicks = 0;
        var startedCount = 0;
        var endedCount = 0;
        button.Click += (_, _) => clicks++;
        button.HoldStarted += (_, _) => startedCount++;
        button.HoldEnded += (_, _) => endedCount++;

        var point = Center(window, button);
        window.MouseDown(point, MouseButton.Left);

        await WaitUntilAsync(() => startedCount == 1);
        startedCount.Should().Be(1, "按住超过阈值应触发 HoldStarted");
        clicks.Should().Be(0, "长按期间本控件不参与重复节奏（由 VM 会话循环驱动）");

        window.MouseUp(point, MouseButton.Left);

        clicks.Should().Be(0, "长按后的松开不得再触发一次 Click");
        endedCount.Should().Be(1, "松开应结束长按会话");

        // 再次短按应恢复正常单击
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        clicks.Should().Be(1, "长按结束后短按恢复正常单击");
        endedCount.Should().Be(1, "短按不触发 HoldEnded");
    }

    [AvaloniaFact]
    public async Task 长按中被禁用_兜底结束会话()
    {
        var (button, window) = CreateButtonWindow();
        var clicks = 0;
        var startedCount = 0;
        var endedCount = 0;
        button.Click += (_, _) => clicks++;
        button.HoldStarted += (_, _) => startedCount++;
        button.HoldEnded += (_, _) => endedCount++;

        var point = Center(window, button);
        window.MouseDown(point, MouseButton.Left);
        await WaitUntilAsync(() => startedCount == 1);

        button.IsEnabled = false;

        endedCount.Should().Be(1, "按住期间被禁用应兜底结束长按，避免会话失去停止信号");
        clicks.Should().Be(0);
    }

    [AvaloniaFact]
    public async Task 长按中移除控件_兜底结束会话且不重复触发()
    {
        var (button, window) = CreateButtonWindow();
        var startedCount = 0;
        var endedCount = 0;
        button.HoldStarted += (_, _) => startedCount++;
        button.HoldEnded += (_, _) => endedCount++;

        var point = Center(window, button);
        window.MouseDown(point, MouseButton.Left);
        await WaitUntilAsync(() => startedCount == 1);

        window.Content = new Border();

        endedCount.Should().Be(1, "控件卸载应兜底结束长按且只触发一次");
    }

    [AvaloniaFact]
    public async Task 按住后移出按钮_松开仍结束会话且不点击()
    {
        var (button, window) = CreateButtonWindow();
        var clicks = 0;
        var startedCount = 0;
        var endedCount = 0;
        button.Click += (_, _) => clicks++;
        button.HoldStarted += (_, _) => startedCount++;
        button.HoldEnded += (_, _) => endedCount++;

        var point = Center(window, button);
        window.MouseDown(point, MouseButton.Left);
        await WaitUntilAsync(() => startedCount == 1);

        // 移出按钮外再松开（指针已隐式捕获，释放仍应送达按钮）
        var outside = new Point(5, 5);
        window.MouseMove(outside);
        window.MouseUp(outside, MouseButton.Left);

        clicks.Should().Be(0, "移出后松开不应触发 Click（长按后的松开被抑制）");
        endedCount.Should().Be(1, "移出后松开仍应结束长按会话");
    }

    [AvaloniaFact]
    public async Task 绑定异步命令_长按期间命令不执行_且按钮不被命令自禁用()
    {
        var button = new HoldRepeatButton { HoldDelay = HoldDelayMs, Width = 120, Height = 40 };
        var executions = 0;
        var command = new AsyncRelayCommand(async () =>
        {
            executions++;
            await Task.Delay(30);
        });
        button.Command = command;

        var startedCount = 0;
        var endedCount = 0;
        button.HoldStarted += (_, _) => startedCount++;
        button.HoldEnded += (_, _) => endedCount++;

        var window = new Window { Width = 200, Height = 100, Content = button };
        window.Show();
        window.UpdateLayout();

        var point = Center(window, button);
        window.MouseDown(point, MouseButton.Left);
        await WaitUntilAsync(() => startedCount == 1);

        executions.Should().Be(0, "长按连续生成不经过命令");
        button.IsEffectivelyEnabled.Should().BeTrue("命令未在执行，按钮不应被 CanExecute=false 自禁用");

        window.MouseUp(point, MouseButton.Left);
        endedCount.Should().Be(1, "松开应结束长按会话");
        executions.Should().Be(0);

        // 长按结束后短按应正常执行一次命令
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        await WaitUntilAsync(() => executions == 1);
        executions.Should().Be(1, "短按仍走命令执行单次生成");
    }
}
