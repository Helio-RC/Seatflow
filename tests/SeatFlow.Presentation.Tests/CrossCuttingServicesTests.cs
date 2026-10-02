using FluentAssertions;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 统一脏检查 <see cref="DirtyTracker"/> 的语义测试（M6）：
/// 基线比较、无基线时的显式标记保留、重置。
/// </summary>
public class DirtyTrackerTests
{
    [Fact]
    public void 初始状态为干净()
    {
        var tracker = new DirtyTracker();

        tracker.IsDirty.Should().BeFalse();
        tracker.CleanSnapshot.Should().BeNull();
    }

    [Fact]
    public void Update_与基线不同时置脏_相同则恢复干净()
    {
        var tracker = new DirtyTracker();
        tracker.MarkClean("A");

        tracker.Update("B");
        tracker.IsDirty.Should().BeTrue();

        tracker.Update("A");
        tracker.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void 无基线时_Update_不改变状态_显式_MarkDirty_保留()
    {
        var tracker = new DirtyTracker();

        tracker.Update("A");
        tracker.IsDirty.Should().BeFalse();

        tracker.MarkDirty();
        tracker.Update("B");
        tracker.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void Reset_清空基线与脏状态()
    {
        var tracker = new DirtyTracker();
        tracker.MarkClean("A");
        tracker.Update("B");

        tracker.Reset();

        tracker.IsDirty.Should().BeFalse();
        tracker.CleanSnapshot.Should().BeNull();
    }

    [Fact]
    public void MarkClean_驱动_IsDirty_变更通知()
    {
        var tracker = new DirtyTracker();
        tracker.MarkClean("A");
        tracker.Update("B");

        var changes = new List<string?>();
        tracker.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        tracker.MarkClean("B");

        changes.Should().Contain(nameof(DirtyTracker.IsDirty));
    }
}

/// <summary>
/// 对话框门 <see cref="DialogGate"/> 的并发语义测试（M6）：
/// 同一时刻只允许一个模态流程，门忙时后续调用立即返回默认值 / false，异常也会释放门。
/// </summary>
public class DialogGateTests
{
    [Fact]
    public async Task 无返回值_并发调用_第二个返回_false_且不执行()
    {
        var gate = new DialogGate();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondExecuted = false;

        var first = gate.RunAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        gate.IsBusy.Should().BeTrue();
        var second = await gate.RunAsync(() =>
        {
            secondExecuted = true;
            return Task.CompletedTask;
        });

        second.Should().BeFalse();
        secondExecuted.Should().BeFalse();

        release.SetResult();
        (await first).Should().BeTrue();
        gate.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task 泛型_门忙时返回_default()
    {
        var gate = new DialogGate();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = gate.RunAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
            return 42;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var second = await gate.RunAsync(() => Task.FromResult(99));
        second.Should().Be(0);

        release.SetResult();
        (await first).Should().Be(42);
    }

    [Fact]
    public async Task 异常后门被释放_可再次执行()
    {
        var gate = new DialogGate();

        var failed = async () =>
        {
            await gate.RunAsync(() => Task.FromException(new InvalidOperationException("boom")));
        };
        await failed.Should().ThrowAsync<InvalidOperationException>();

        gate.IsBusy.Should().BeFalse();
        var result = await gate.RunAsync(() => Task.FromResult(7));
        result.Should().Be(7);
    }
}
