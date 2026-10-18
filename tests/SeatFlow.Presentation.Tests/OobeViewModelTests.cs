using FluentAssertions;
using NSubstitute;
using ReactiveUI;
using ReactiveUI.Primitives;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Core.Telemetry;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Infrastructure.Layouts;

namespace SeatFlow.Presentation.Tests;

/// <summary>OOBE 状态机：首启/重跑、遥测单次落盘、名单保存与完成标记。</summary>
public class OobeViewModelTests
{
    private sealed class Harness
    {
        public required OobeViewModel ViewModel { get; init; }
        public required IApplicationFacade Facade { get; init; }
        public required ITelemetryService Telemetry { get; init; }
        public required AppSettings Settings { get; init; }
    }

    private static Harness CreateHarness(bool firstLaunch = true, bool consentShown = false)
    {
        var settings = new AppSettings
        {
            IsFirstLaunch = firstLaunch,
            Telemetry = new TelemetryConfig { Enabled = true, ConsentShown = consentShown },
            AutoUpdate = AutoUpdateMode.AutoUpdate,
        };

        var facade = Substitute.For<IApplicationFacade>();
        facade.LoadAppSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(settings));
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<StudentDatasetInfo>>([]));
        facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<VenueSummary>>([]));

        var telemetry = Substitute.For<ITelemetryService>();
        var vm = new OobeViewModel(
            facade,
            Substitute.For<IFileService>(),
            telemetry,
            Substitute.For<INavigationService>(),
            new ShellLayoutService(),
            new StartupRestoreState(),
            NullDialogService.Instance);

        return new Harness
        {
            ViewModel = vm,
            Facade = facade,
            Telemetry = telemetry,
            Settings = settings,
        };
    }

    private static async Task ExecuteAsync(ReactiveCommand<RxVoid, RxVoid> command)
    {
        command.Execute().SubscribeSafe(_ => { });
        await Task.Delay(80);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var started = Environment.TickCount64;
        while (!condition() && Environment.TickCount64 - started < timeoutMs)
            await Task.Delay(15);

        condition().Should().BeTrue($"等待条件超时（{timeoutMs}ms）");
    }

    [Fact]
    public async Task 首启_从欢迎开始_遥测默认勾选且继续后单次落盘()
    {
        var h = CreateHarness();
        await h.ViewModel.StartAsync();

        h.ViewModel.Step.Should().Be(OobeStep.Welcome);
        h.ViewModel.IsVisible.Should().BeTrue();
        h.ViewModel.ShowTelemetryQuestion.Should().BeTrue();
        h.ViewModel.TelemetryEnabled.Should().BeTrue();
        h.Telemetry.DidNotReceive().ApplyEnabled(Arg.Any<bool>());

        await ExecuteAsync(h.ViewModel.NextCommand);
        await WaitUntilAsync(() => h.ViewModel.Step == OobeStep.Preferences);

        h.Settings.Telemetry.ConsentShown.Should().BeTrue();
        h.Settings.Telemetry.Enabled.Should().BeTrue();
        h.ViewModel.ShowTelemetryQuestion.Should().BeFalse();
        h.Telemetry.Received(1).ApplyEnabled(true);
        await h.Facade.Received(1).SaveAppSettingsAsync(Arg.Any<AppSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 重跑_从名单开始_不重复询问遥测()
    {
        var h = CreateHarness(firstLaunch: true, consentShown: true);
        await h.ViewModel.StartAsync(isRestart: true);

        h.ViewModel.Step.Should().Be(OobeStep.Roster);
        h.ViewModel.ShowTelemetryQuestion.Should().BeFalse();
        h.ViewModel.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public async Task 完成_写入IsFirstLaunchfalse并触发Completed()
    {
        var h = CreateHarness();
        await h.ViewModel.StartAsync();
        var completed = false;
        h.ViewModel.Completed += (_, _) => completed = true;

        await h.ViewModel.CompleteAsync(skipped: false);

        h.Settings.IsFirstLaunch.Should().BeFalse();
        h.ViewModel.IsVisible.Should().BeFalse();
        h.ViewModel.IsActive.Should().BeFalse();
        completed.Should().BeTrue();
    }

    [Fact]
    public async Task 空白名单_保存数据集后进入会场()
    {
        var h = CreateHarness();
        await h.ViewModel.StartAsync(isRestart: true);

        h.ViewModel.StartBlankRosterCommand.Execute().SubscribeSafe(_ => { });
        h.ViewModel.RosterRows.Should().BeEmpty("空白名单从 0 行开始，由底部新增行输入");
        h.ViewModel.NewStudent.Name = "张三";
        h.ViewModel.AddNewStudentCommand.Execute().SubscribeSafe(_ => { });
        h.ViewModel.DatasetName = "高一(1)班";

        h.Facade.SaveStudentDatasetAsync(
                Arg.Any<string>(), Arg.Any<List<Student>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("ds-1");

        await ExecuteAsync(h.ViewModel.NextCommand);
        await WaitUntilAsync(() => h.ViewModel.Step == OobeStep.Venue);

        h.ViewModel.SelectedDatasetId.Should().Be("ds-1");
        await h.Facade.Received(1).SaveStudentDatasetAsync(
            "高一(1)班", Arg.Is<List<Student>>(s => s.Count == 1 && s[0].Name == "张三"),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }


    [Fact]
    public async Task 重进名单步骤_保留已载入数据集_不重复创建()
    {
        var h = CreateHarness();
        var dataset = new StudentDatasetInfo { Id = "ds-1", Name = "已有班级", StudentCount = 1 };
        h.Facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<StudentDatasetInfo>>([dataset]));
        h.Facade.LoadStudentDatasetAsync("ds-1", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<List<Student>?>([new Student { Name = "李四" }]));

        await h.ViewModel.StartAsync(isRestart: true);
        h.ViewModel.UseExistingRosterCommand.Execute(dataset).SubscribeSafe(_ => { });
        await WaitUntilAsync(() => h.ViewModel.SelectedDatasetId == "ds-1");

        await ExecuteAsync(h.ViewModel.NextCommand);
        await WaitUntilAsync(() => h.ViewModel.Step == OobeStep.Venue);
        await ExecuteAsync(h.ViewModel.BackCommand);
        await WaitUntilAsync(() => h.ViewModel.Step == OobeStep.Roster);

        h.ViewModel.RosterPrimaryText.Should().Be(Resources.Oobe_UseRoster);
        h.Facade.ClearReceivedCalls();
        h.Facade.SaveStudentDatasetAsync(
                Arg.Any<string>(), Arg.Any<List<Student>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("unexpected");
        h.Facade.UpdateStudentDatasetAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<List<Student>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await ExecuteAsync(h.ViewModel.NextCommand);
        await WaitUntilAsync(() => h.ViewModel.Step == OobeStep.Venue);

        await h.Facade.DidNotReceive().SaveStudentDatasetAsync(
            Arg.Any<string>(), Arg.Any<List<Student>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await h.Facade.DidNotReceive().UpdateStudentDatasetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<List<Student>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 已有空名单_不能继续()
    {
        var h = CreateHarness();
        var dataset = new StudentDatasetInfo { Id = "ds-empty", Name = "空班级", StudentCount = 0 };
        h.Facade.LoadStudentDatasetAsync("ds-empty", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<List<Student>?>([]));

        await h.ViewModel.StartAsync(isRestart: true);
        h.ViewModel.UseExistingRosterCommand.Execute(dataset).SubscribeSafe(_ => { });
        await WaitUntilAsync(() => h.ViewModel.SelectedDatasetId == "ds-empty" && !h.ViewModel.IsRosterLoading);

        h.ViewModel.HasRoster.Should().BeFalse();
        h.ViewModel.CanProceed.Should().BeFalse("空名单不能进入交接");
    }

    [Fact]
    public async Task 名单保存进行中_重复点击不会重复创建()
    {
        var h = CreateHarness();
        await h.ViewModel.StartAsync(isRestart: true);
        h.ViewModel.StartBlankRosterCommand.Execute().SubscribeSafe(_ => { });
        h.ViewModel.NewStudent.Name = "张三";
        h.ViewModel.AddNewStudentCommand.Execute().SubscribeSafe(_ => { });

        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Facade.SaveStudentDatasetAsync(
                Arg.Any<string>(), Arg.Any<List<Student>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => gate.Task);

        h.ViewModel.NextCommand.Execute().SubscribeSafe(_ => { });
        await WaitUntilAsync(() => h.ViewModel.IsBusy);

        h.ViewModel.CanProceed.Should().BeFalse("保存期间禁止再次推进");
        ((System.Windows.Input.ICommand)h.ViewModel.NextCommand).CanExecute(null).Should().BeFalse();

        gate.SetResult("ds-1");
        await WaitUntilAsync(() => !h.ViewModel.IsBusy);

        await h.Facade.Received(1).SaveStudentDatasetAsync(
            Arg.Any<string>(), Arg.Any<List<Student>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 编辑已有网格会场_保留障碍物与座位ID()
    {
        var h = CreateHarness();
        var summary = new VenueSummary("venue-1", "旧会场");
        h.Facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<VenueSummary>>([summary]));

        var meta = new GridLayoutMetadata
        {
            Rows = 2,
            Columns = 2,
            SeatsPerDesk = 1,
            HorizontalSpacing = 40,
            VerticalSpacing = 36,
            OriginX = 200,
            OriginY = 200,
            IntraDeskSpacing = 12,
            InterDeskSpacing = 32,
        };
        var layout = GridLayoutBuilder.BuildGrid(meta);
        layout.Id = "venue-1";
        layout.Name = "旧会场";
        foreach (var seat in layout.Seats.OfType<GridSeat>())
            seat.Id = $"old-{seat.Row}-{seat.Column}";
        layout.Obstacles.Add(new Obstacle { Type = "Podium", X = 10, Y = 10, Width = 60, Height = 40 });
        layout.Obstacles.Add(new Obstacle { Type = "Door", X = 30, Y = 40, Width = 36, Height = 24 });
        h.Facade.LoadVenueAsync("venue-1", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<ClassroomLayoutDefinition?>(layout));

        ClassroomLayoutDefinition? saved = null;
        h.Facade.SaveVenueAsync(
                Arg.Any<string>(), Arg.Do<ClassroomLayoutDefinition>(l => saved = l), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await h.ViewModel.StartAsync(isRestart: true);
        h.ViewModel.Step = OobeStep.Venue;
        h.ViewModel.SelectExistingVenueCommand.Execute(summary).SubscribeSafe(_ => { });
        await WaitUntilAsync(() => h.ViewModel.IsEditingExistingVenue && !h.ViewModel.IsVenueLoading);

        h.ViewModel.Rows = 3;
        await ExecuteAsync(h.ViewModel.NextCommand);
        await WaitUntilAsync(() => saved is not null);

        saved!.Obstacles.Should().HaveCount(2);
        saved.Obstacles.Should().Contain(o => o.Type == "Door");
        saved.Obstacles.Should().Contain(o => o.Type == "Podium");
        saved.Seats.OfType<GridSeat>().Single(s => s.Row == 1 && s.Column == 1)
            .Id.Should().Be("old-1-1", "已有会场必须按位置复用座位 ID");
    }

    [Fact]
    public async Task 选择未载入的会场_不能继续_加载失败回退()
    {
        var h = CreateHarness();
        var a = new VenueSummary("venue-a", "A 会场");
        var b = new VenueSummary("venue-b", "B 会场");
        h.Facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<VenueSummary>>([a, b]));

        var layout = GridLayoutBuilder.BuildGrid(new GridLayoutMetadata { Rows = 2, Columns = 2, SeatsPerDesk = 1 });
        layout.Id = a.Id;
        layout.Name = a.Name;
        h.Facade.LoadVenueAsync(a.Id, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<ClassroomLayoutDefinition?>(layout));
        h.Facade.LoadVenueAsync(b.Id, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<ClassroomLayoutDefinition?>(null));

        await h.ViewModel.StartAsync(isRestart: true);
        h.ViewModel.Step = OobeStep.Venue;
        h.ViewModel.SelectExistingVenueCommand.Execute(a).SubscribeSafe(_ => { });
        await WaitUntilAsync(() => h.ViewModel.IsEditingExistingVenue && !h.ViewModel.IsVenueLoading);

        h.ViewModel.SelectedExistingVenue = b;
        h.ViewModel.CanProceed.Should().BeFalse("列表选择不等于已载入");

        h.ViewModel.SelectExistingVenueCommand.Execute(b).SubscribeSafe(_ => { });
        await WaitUntilAsync(() => !h.ViewModel.IsVenueLoading && h.ViewModel.VenueError is not null);

        h.ViewModel.SelectedExistingVenue!.Id.Should().Be(a.Id, "加载失败应回退到已载入会场");
        h.ViewModel.CanProceed.Should().BeTrue();
    }

    [Fact]
    public async Task 进入会场步骤_默认新建而不是自动选中上次会场()
    {
        var h = CreateHarness();
        h.Settings.LastVenueId = "old-venue";
        var summary = new VenueSummary("old-venue", "旧会场");
        h.Facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<VenueSummary>>([summary]));
        var oldLayout = GridLayoutBuilder.BuildGrid(new GridLayoutMetadata
        {
            Rows = 5,
            Columns = 8,
            HorizontalSpacing = 40,
            VerticalSpacing = 36,
            IntraDeskSpacing = 12,
            PodiumWidth = 60,
        });
        oldLayout.Id = "old-venue";
        oldLayout.Name = "旧会场";
        h.Facade.LoadVenueAsync("old-venue", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<ClassroomLayoutDefinition?>(oldLayout));
        h.Facade.SaveStudentDatasetAsync(
                Arg.Any<string>(), Arg.Any<List<Student>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("ds-1");

        await h.ViewModel.StartAsync(isRestart: true);
        h.ViewModel.StartBlankRosterCommand.Execute().SubscribeSafe(_ => { });
        h.ViewModel.NewStudent.Name = "张三";
        h.ViewModel.AddNewStudentCommand.Execute().SubscribeSafe(_ => { });
        await ExecuteAsync(h.ViewModel.NextCommand);
        await WaitUntilAsync(() => h.ViewModel.Step == OobeStep.Venue
            && h.ViewModel.ExistingVenues.Count == 1);

        h.ViewModel.SelectedExistingVenue.Should().BeNull("OOBE 默认应创建新会场");
        h.ViewModel.IsEditingExistingVenue.Should().BeFalse();
        h.ViewModel.VenueName.Should().MatchRegex("^新会场_[0-9a-f]{8}$");
        await h.Facade.DidNotReceive().LoadVenueAsync("old-venue", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 新建会场_八位ID_会场页命名_统一默认参数()
    {
        var h = CreateHarness();
        await h.ViewModel.StartAsync(isRestart: true);
        h.ViewModel.Step = OobeStep.Venue;

        h.ViewModel.VenueName.Should().MatchRegex("^新会场_[0-9a-f]{8}$");
        var expectedName = h.ViewModel.VenueName;

        string? savedId = null;
        ClassroomLayoutDefinition? saved = null;
        h.Facade.SaveVenueAsync(
                Arg.Do<string>(id => savedId = id),
                Arg.Do<ClassroomLayoutDefinition>(l => saved = l),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await ExecuteAsync(h.ViewModel.NextCommand);
        await WaitUntilAsync(() => saved is not null);

        savedId.Should().NotBeNull();
        savedId!.Should().HaveLength(8, "会场 ID 应与会场页一致使用 8 位");
        saved!.Id.Should().Be(savedId);
        saved.Name.Should().Be(expectedName);
        saved.Name.Should().Be($"新会场_{savedId}");

        var grid = saved.Metadata.Should().BeOfType<GridLayoutMetadata>().Subject;
        grid.Rows.Should().Be(VenueLayoutDefaults.GridRows);
        grid.Columns.Should().Be(VenueLayoutDefaults.GridColumns);
        grid.HorizontalSpacing.Should().Be(VenueLayoutDefaults.GridHorizontalSpacing);
        grid.VerticalSpacing.Should().Be(VenueLayoutDefaults.GridVerticalSpacing);
        grid.OriginX.Should().Be(VenueLayoutDefaults.GridOriginX);
        grid.OriginY.Should().Be(VenueLayoutDefaults.GridOriginY);
        grid.SeatsPerDesk.Should().Be(VenueLayoutDefaults.GridSeatsPerDesk);
        grid.IntraDeskSpacing.Should().Be(VenueLayoutDefaults.GridIntraDeskSpacing);
        grid.InterDeskSpacing.Should().Be(VenueLayoutDefaults.GridInterDeskSpacing);
        grid.AisleWidth.Should().Be(VenueLayoutDefaults.GridAisleWidth);
        grid.FrontRowCount.Should().Be(VenueLayoutDefaults.GridFrontRowCount);
        grid.HasPodium.Should().Be(VenueLayoutDefaults.GridHasPodium);
        grid.PodiumWidth.Should().Be(VenueLayoutDefaults.GridPodiumWidth);
        grid.PodiumHeight.Should().Be(VenueLayoutDefaults.GridPodiumHeight);
    }

    [Fact]
    public async Task 从已有会场切到新建会场_使用统一默认参数()
    {
        var h = CreateHarness();
        var summary = new VenueSummary("old-venue", "旧会场");
        h.Facade.ListVenueSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<VenueSummary>>([summary]));

        var oldMeta = new GridLayoutMetadata
        {
            Rows = 5,
            Columns = 8,
            HorizontalSpacing = 40,
            VerticalSpacing = 36,
            IntraDeskSpacing = 12,
            InterDeskSpacing = 32,
            PodiumWidth = 60,
        };
        var oldLayout = GridLayoutBuilder.BuildGrid(oldMeta);
        oldLayout.Id = "old-venue";
        oldLayout.Name = "旧会场";
        h.Facade.LoadVenueAsync("old-venue", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<ClassroomLayoutDefinition?>(oldLayout));

        await h.ViewModel.StartAsync(isRestart: true);
        h.ViewModel.Step = OobeStep.Venue;
        h.ViewModel.SelectExistingVenueCommand.Execute(summary).SubscribeSafe(_ => { });
        await WaitUntilAsync(() => h.ViewModel.IsEditingExistingVenue && !h.ViewModel.IsVenueLoading);

        h.ViewModel.NewVenueCommand.Execute().SubscribeSafe(_ => { });
        h.ViewModel.VenueName.Should().MatchRegex("^新会场_[0-9a-f]{8}$");

        ClassroomLayoutDefinition? saved = null;
        h.Facade.SaveVenueAsync(
                Arg.Any<string>(),
                Arg.Do<ClassroomLayoutDefinition>(l => saved = l),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await ExecuteAsync(h.ViewModel.NextCommand);
        await WaitUntilAsync(() => saved is not null);

        var grid = saved!.Metadata.Should().BeOfType<GridLayoutMetadata>().Subject;
        grid.HorizontalSpacing.Should().Be(VenueLayoutDefaults.GridHorizontalSpacing);
        grid.VerticalSpacing.Should().Be(VenueLayoutDefaults.GridVerticalSpacing);
        grid.IntraDeskSpacing.Should().Be(VenueLayoutDefaults.GridIntraDeskSpacing);
        grid.PodiumWidth.Should().Be(VenueLayoutDefaults.GridPodiumWidth);
    }

    [Fact]
    public async Task 新建会场重复保存_复用同一会场与座位ID()
    {
        var h = CreateHarness();
        var saved = new List<ClassroomLayoutDefinition>();
        h.Facade.SaveVenueAsync(
                Arg.Any<string>(), Arg.Do<ClassroomLayoutDefinition>(l => saved.Add(l)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await h.ViewModel.StartAsync(isRestart: true);
        h.ViewModel.Step = OobeStep.Venue;
        h.ViewModel.Rows = 2;
        h.ViewModel.Columns = 2;
        h.ViewModel.VenueName = "新会场";

        await ExecuteAsync(h.ViewModel.NextCommand);
        await WaitUntilAsync(() => saved.Count == 1);
        h.ViewModel.Rows = 3;
        await ExecuteAsync(h.ViewModel.NextCommand);
        await WaitUntilAsync(() => saved.Count == 2);

        saved[1].Id.Should().Be(saved[0].Id, "重复保存应覆盖同一会场");
        var firstIds = saved[0].Seats.OfType<GridSeat>().ToDictionary(s => (s.Row, s.Column), s => s.Id);
        foreach (var seat in saved[1].Seats.OfType<GridSeat>())
        {
            if (firstIds.TryGetValue((seat.Row, seat.Column), out var id))
                seat.Id.Should().Be(id, "未变化的位置应复用座位 ID");
        }
    }

    [Fact]
    public async Task 共享表格新增行命令_支持ICommand空参数()
    {
        var h = CreateHarness();
        await h.ViewModel.StartAsync(isRestart: true);
        h.ViewModel.StartBlankRosterCommand.Execute().SubscribeSafe(_ => { });

        h.ViewModel.NewStudent.Name = "张三";
        ((System.Windows.Input.ICommand)h.ViewModel.AddNewStudentCommand).Execute(null);

        h.ViewModel.RosterRows.Should().ContainSingle(r => r.Name == "张三");
        h.ViewModel.NewStudent.Name.Should().BeEmpty("提交后底部新增行应清空");
    }

    [Fact]
    public async Task 已有名单_编辑后原地更新()
    {
        var h = CreateHarness();
        var dataset = new StudentDatasetInfo { Id = "ds-existing", Name = "已有班级", StudentCount = 1 };
        h.Facade.LoadStudentDatasetAsync("ds-existing", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<List<Student>?>([new Student { Name = "李四" }]));

        await h.ViewModel.StartAsync(isRestart: true);
        h.ViewModel.UseExistingRosterCommand.Execute(dataset).SubscribeSafe(_ => { });
        await WaitUntilAsync(() => h.ViewModel.SelectedDatasetId == "ds-existing");

        h.ViewModel.RosterRows.Should().ContainSingle();
        var row = h.ViewModel.RosterRows[0];
        h.ViewModel.BeginEditCommand.Execute(row).SubscribeSafe(_ => { });
        row.IsEditing.Should().BeTrue("已有名单也走共享表格的行内编辑");
        row.NameText = "王五";
        h.ViewModel.EndEditCommand.Execute(row).SubscribeSafe(_ => { });
        h.ViewModel.DatasetName = "已有班级(改)";

        h.Facade.ClearReceivedCalls();
        h.Facade.UpdateStudentDatasetAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<List<Student>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await ExecuteAsync(h.ViewModel.NextCommand);
        await WaitUntilAsync(() => h.ViewModel.Step == OobeStep.Venue);

        await h.Facade.Received(1).UpdateStudentDatasetAsync(
            "ds-existing", "已有班级(改)",
            Arg.Is<List<Student>>(s => s.Count == 1 && s[0].Name == "王五"),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await h.Facade.DidNotReceive().SaveStudentDatasetAsync(
            Arg.Any<string>(), Arg.Any<List<Student>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 已有名单_编辑后保持导入顺序不重排()
    {
        var h = CreateHarness();
        var dataset = new StudentDatasetInfo { Id = "ds-order", Name = "顺序班级", StudentCount = 2 };
        h.Facade.LoadStudentDatasetAsync("ds-order", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<List<Student>?>([
                new Student { Name = "张三" },
                new Student { Name = "李四" },
            ]));

        await h.ViewModel.StartAsync(isRestart: true);
        h.ViewModel.UseExistingRosterCommand.Execute(dataset).SubscribeSafe(_ => { });
        await WaitUntilAsync(() => h.ViewModel.SelectedDatasetId == "ds-order");

        var second = h.ViewModel.RosterRows[1];
        h.ViewModel.BeginEditCommand.Execute(second).SubscribeSafe(_ => { });
        second.NameText = "A同学";
        h.ViewModel.EndEditCommand.Execute(second).SubscribeSafe(_ => { });

        h.ViewModel.RosterRows.Select(r => r.Name).Should().Equal(
            new[] { "张三", "A同学" },
            "OOBE 保留导入顺序，不按姓名重排");
    }

    [Fact]
    public async Task 跳过设置_不按默认勾选启用遥测()
    {
        var h = CreateHarness();
        await h.ViewModel.StartAsync();
        h.ViewModel.TelemetryEnabled.Should().BeTrue("欢迎屏复选框默认勾选");

        h.ViewModel.SkipAllCommand.Execute().SubscribeSafe(_ => { });
        await WaitUntilAsync(() => !h.ViewModel.IsVisible);

        h.Settings.Telemetry.ConsentShown.Should().BeTrue();
        h.Settings.Telemetry.Enabled.Should().BeFalse("跳过设置不能被默认勾选静默视为同意遥测");
        h.ViewModel.TelemetryEnabled.Should().BeFalse();
        h.Telemetry.Received(1).ApplyEnabled(false);
    }

    [Fact]
    public async Task 会场保存期间被修改_不进入交接且提示重新保存()
    {
        var h = CreateHarness();
        await h.ViewModel.StartAsync(isRestart: true);
        h.ViewModel.Step = OobeStep.Venue;

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Facade.SaveVenueAsync(
                Arg.Any<string>(), Arg.Any<ClassroomLayoutDefinition>(), Arg.Any<CancellationToken>())
            .Returns(_ => gate.Task);

        h.ViewModel.NextCommand.Execute().SubscribeSafe(_ => { });
        await WaitUntilAsync(() => h.ViewModel.IsBusy);

        h.ViewModel.Rows = 7;
        gate.SetResult();
        await WaitUntilAsync(() => !h.ViewModel.IsBusy);

        h.ViewModel.Step.Should().Be(OobeStep.Venue, "保存期间被修改时应停留在会场步骤");
        h.ViewModel.VenueError.Should().NotBeNullOrEmpty();
        h.ViewModel.VenueError.Should().Be(Resources.Oobe_ErrorVenueChanged);
    }
}
