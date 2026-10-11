using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using SeatFlow.Presentation.Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Presentation.Avalonia.Views;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 排座工作台长按连续生成会话：
/// 顺序执行、会话内计数只加一次、逐策略进度消息被抑制、松手（含在途）即停、异常中止、离开页面停止。
/// </summary>
public class SeatingContinuousGenerateTests
{
    private const string VenueId = "v1";
    private const string DatasetId = "d1";

    private static ClassroomLayoutDefinition CreateLayout() => new()
    {
        Id = VenueId,
        Name = "测试会场",
        Metadata = new GridLayoutMetadata { Rows = 1, Columns = 1 },
    };

    private static IReadOnlyList<Student> CreateStudents() => [new Student { Id = "s1", Name = "张三" }];

    private static SeatingWorkspace CreateWorkspace()
        => new(CreateStudents(), new Seat[] { new GridSeat { Id = "seat1", Row = 1, Column = 1 } });

    private static (SeatingArrangementViewModel Vm, IApplicationFacade Facade, IArrangementCounterService Counter) CreateVm()
    {
        var facade = Substitute.For<IApplicationFacade>();
        var counter = Substitute.For<IArrangementCounterService>();
        facade.LoadVenueAsync(VenueId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ClassroomLayoutDefinition?>(CreateLayout()));
        facade.LoadStudentDatasetAsync(DatasetId, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<List<Student>?>(new List<Student> { new() { Id = "s1", Name = "张三" } }));
        facade.GetStrategiesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<StrategyDisplayInfo>()));

        var vm = new SeatingArrangementViewModel(
            facade,
            Substitute.For<IFileService>(),
            counter,
            new ShellLayoutService(),
            new WelcomeCardViewModel(NullDialogService.Instance),
            Substitute.For<IServiceProvider>(),
            NullDialogService.Instance);
        return (vm, facade, counter);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var started = Environment.TickCount64;
        while (!condition() && Environment.TickCount64 - started < timeoutMs)
            await Task.Delay(15);
    }

    /// <summary>选中会场与名单，并等待会场布局异步加载完成（_currentLayout 就绪）。</summary>
    private static async Task PrepareSelectedAsync(SeatingArrangementViewModel vm)
    {
        vm.SelectedVenue = new VenueItem(VenueId, "测试会场");
        await WaitUntilAsync(() => vm.StatusMessage.Contains("测试会场"), timeoutMs: 3000);
        vm.SelectedDataset = new StudentDatasetInfo { Id = DatasetId, Name = "测试名单" };
    }

    [AvaloniaFact]
    public async Task 连续会话_顺序执行_计数只加一次_第2次后停止()
    {
        var (vm, facade, counter) = CreateVm();
        facade.GenerateSeatingAsync(Arg.Any<SeatingRequest>(), Arg.Any<IProgress<SeatingProgress>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(CreateWorkspace()));
        await PrepareSelectedAsync(vm);

        // 第 2 次完成后立即停止（避免继续发起第 3 次）
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.StatusMessage) && vm.StatusMessage.Contains("第 2 次"))
                vm.StopContinuousGenerate();
        };

        await vm.StartContinuousGenerateAsync();

        vm.IsContinuousGenerating.Should().BeFalse();
        vm.StatusMessage.Should().Contain("座位安排已生成", "会话结束后应恢复标准消息");
        await facade.Received(2).GenerateSeatingAsync(Arg.Any<SeatingRequest>(), Arg.Any<IProgress<SeatingProgress>>(), Arg.Any<CancellationToken>());
        counter.Received(1).Increment();
    }

    [AvaloniaFact]
    public async Task 松手时_在途生成跑完即停()
    {
        var (vm, facade, counter) = CreateVm();
        var gate = new TaskCompletionSource<SeatingWorkspace>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        facade.GenerateSeatingAsync(Arg.Any<SeatingRequest>(), Arg.Any<IProgress<SeatingProgress>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref calls) == 1
                ? Task.FromResult(CreateWorkspace())
                : gate.Task);
        await PrepareSelectedAsync(vm);

        var session = vm.StartContinuousGenerateAsync();
        await WaitUntilAsync(() => Volatile.Read(ref calls) == 2);

        vm.StopContinuousGenerate();
        gate.SetResult(CreateWorkspace());
        await session;

        await facade.Received(2).GenerateSeatingAsync(Arg.Any<SeatingRequest>(), Arg.Any<IProgress<SeatingProgress>>(), Arg.Any<CancellationToken>());
        counter.Received(1).Increment();
        vm.StatusMessage.Should().Contain("座位安排已生成", "在途那次应用结果后应恢复标准消息");
    }

    [AvaloniaFact]
    public async Task 生成异常_中止会话()
    {
        var (vm, facade, counter) = CreateVm();
        var calls = 0;
        facade.GenerateSeatingAsync(Arg.Any<SeatingRequest>(), Arg.Any<IProgress<SeatingProgress>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref calls) == 1
                ? Task.FromResult(CreateWorkspace())
                : Task.FromException<SeatingWorkspace>(new InvalidOperationException("boom")));
        await PrepareSelectedAsync(vm);

        await vm.StartContinuousGenerateAsync();

        await facade.Received(2).GenerateSeatingAsync(Arg.Any<SeatingRequest>(), Arg.Any<IProgress<SeatingProgress>>(), Arg.Any<CancellationToken>());
        counter.Received(1).Increment();
        vm.IsContinuousGenerating.Should().BeFalse();
        vm.StatusMessage.Should().Contain("座位安排已生成", "失败中止后恢复标准消息，描述画布上的最后一次成功结果");
    }

    [AvaloniaFact]
    public async Task 离开页面_停止会话()
    {
        var (vm, facade, _) = CreateVm();
        var gate = new TaskCompletionSource<SeatingWorkspace>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        facade.GenerateSeatingAsync(Arg.Any<SeatingRequest>(), Arg.Any<IProgress<SeatingProgress>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref calls);
                return gate.Task;
            });
        await PrepareSelectedAsync(vm);

        var session = vm.StartContinuousGenerateAsync();
        await WaitUntilAsync(() => Volatile.Read(ref calls) == 1);

        await vm.OnLeaveAsync();
        gate.SetResult(CreateWorkspace());
        await session;

        await facade.Received(1).GenerateSeatingAsync(Arg.Any<SeatingRequest>(), Arg.Any<IProgress<SeatingProgress>>(), Arg.Any<CancellationToken>());
        vm.IsContinuousGenerating.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task 会话期间_逐策略进度消息被抑制()
    {
        var (vm, facade, _) = CreateVm();
        facade.GenerateSeatingAsync(Arg.Any<SeatingRequest>(), Arg.Any<IProgress<SeatingProgress>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                ci.Arg<IProgress<SeatingProgress>>()?.Report(new SeatingProgress { StatusMessage = "逐策略进度占位" });
                return Task.FromResult(CreateWorkspace());
            });
        await PrepareSelectedAsync(vm);

        var seen = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(vm.StatusMessage)) return;
            seen.Add(vm.StatusMessage);
            if (vm.StatusMessage.Contains("第 1 次")) vm.StopContinuousGenerate();
        };

        await vm.StartContinuousGenerateAsync();
        // 冲刷已排队的进度回调（若未被抑制会在会话结束后迟到覆盖）
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        seen.Should().NotContain(m => m != null && m.Contains("逐策略进度占位"));
        vm.StatusMessage.Should().Contain("座位安排已生成");
    }

    [AvaloniaFact]
    public void 会话期间_生成按钮保持可用()
    {
        var (vm, _, _) = CreateVm();
        vm.SelectedVenue = new VenueItem(VenueId, "测试会场");
        vm.SelectedDataset = new StudentDatasetInfo { Id = DatasetId, Name = "测试名单" };

        vm.IsGenerating = true;
        vm.CanPressGenerate.Should().BeFalse("非会话的生成中按钮保持禁用");

        vm.IsContinuousGenerating = true;
        vm.CanPressGenerate.Should().BeTrue("连续会话期间按钮保持可用，避免按住时丢失指针事件");
    }

    [AvaloniaFact]
    public async Task 再次长按_进入新会话_计数再次加一()
    {
        var (vm, facade, counter) = CreateVm();
        facade.GenerateSeatingAsync(Arg.Any<SeatingRequest>(), Arg.Any<IProgress<SeatingProgress>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(CreateWorkspace()));
        await PrepareSelectedAsync(vm);

        // 每次会话在第 1 次完成后立即停止
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.StatusMessage) && vm.StatusMessage.Contains("第 1 次"))
                vm.StopContinuousGenerate();
        };

        await vm.StartContinuousGenerateAsync();
        counter.Received(1).Increment();

        await vm.StartContinuousGenerateAsync();
        counter.Received(2).Increment();
        await facade.Received(2).GenerateSeatingAsync(Arg.Any<SeatingRequest>(), Arg.Any<IProgress<SeatingProgress>>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task 短按命令_单次生成_每次计数()
    {
        var (vm, facade, counter) = CreateVm();
        facade.GenerateSeatingAsync(Arg.Any<SeatingRequest>(), Arg.Any<IProgress<SeatingProgress>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(CreateWorkspace()));
        await PrepareSelectedAsync(vm);

        await vm.GenerateSeatingCommand.ExecuteAsync(null);

        vm.HasGenerated.Should().BeTrue();
        vm.IsContinuousGenerating.Should().BeFalse();
        vm.StatusMessage.Should().Contain("座位安排已生成");
        counter.Received(1).Increment();

        await vm.GenerateSeatingCommand.ExecuteAsync(null);
        counter.Received(2).Increment();
    }

    [AvaloniaFact]
    public async Task 已有结果时_连续生成期间画布保持可见()
    {
        var (vm, facade, _) = CreateVm();
        var gate = new TaskCompletionSource<SeatingWorkspace>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        facade.GenerateSeatingAsync(Arg.Any<SeatingRequest>(), Arg.Any<IProgress<SeatingProgress>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref calls) == 1
                ? Task.FromResult(CreateWorkspace())
                : gate.Task);
        await PrepareSelectedAsync(vm);

        await vm.GenerateSeatingCommand.ExecuteAsync(null);
        vm.HasGenerated.Should().BeTrue();

        var session = vm.StartContinuousGenerateAsync();
        await WaitUntilAsync(() => Volatile.Read(ref calls) == 2);
        vm.HasGenerated.Should().BeTrue("已有结果时重新生成/连续生成期间不闪现欢迎卡");

        vm.StopContinuousGenerate();
        gate.SetResult(CreateWorkspace());
        await session;

        vm.HasGenerated.Should().BeTrue();
    }

    [AvaloniaFact]
    public void 欢迎卡生成按钮_为长按控件_会话期间保持可用()
    {
        var (vm, facade, _) = CreateVm();
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<VenueSummary>>([]));
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StudentDatasetInfo>>([]));
        facade.LoadAppSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AppSettings()));

        var view = new SeatingArrangementView { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();

        var welcome = view.FindControl<HoldRepeatButton>("WelcomeGenerateButton");
        welcome.Should().NotBeNull("欢迎卡生成按钮应为长按控件");
        welcome!.IsEffectivelyVisible.Should().BeTrue("未生成时欢迎卡可见");

        vm.SelectedVenue = new VenueItem(VenueId, "测试会场");
        vm.SelectedDataset = new StudentDatasetInfo { Id = DatasetId, Name = "测试名单" };

        vm.IsGenerating = true;
        vm.IsContinuousGenerating = true;
        window.UpdateLayout();
        welcome.IsEnabled.Should().BeTrue("连续会话期间欢迎卡按钮保持可用（绑定 CanPressGenerate）");

        vm.IsContinuousGenerating = false;
        window.UpdateLayout();
        welcome.IsEnabled.Should().BeFalse("非会话的生成中保持禁用");

        window.Close();
    }
}
