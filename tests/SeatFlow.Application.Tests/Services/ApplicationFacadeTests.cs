using SeatFlow.Core.Exporters;
using SeatFlow.Core.Interfaces;
using SeatFlow.Core.Providers;
using SeatFlow.Core.Services;
using SeatFlow.Infrastructure.Migration;
using SeatFlow.Infrastructure.Providers;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Application.Tests.Services;

public class ApplicationFacadeTests
{
    private static ApplicationFacade CreateFacade(
        out IServiceProvider serviceProvider,
        out ISeatingSnapshotRepository snapshotRepo,
        out ISeatingPlanExporter exporter,
        out IAppSettingsRepository appSettingsRepo,
        out IVenueRepository venueRepo,
        out IStudentDatasetRepository datasetRepo,
        out StrategyManifestProvider manifestProvider,
        out StrategyConfigFileRepository strategyConfigRepo,
        out StrategyDatasetConfigRepository datasetConfigRepo,
        out ISeatSetsService seatSetsService,
        out ILogger<ApplicationFacade> logger)
    {
        serviceProvider = Substitute.For<IServiceProvider>();
        snapshotRepo = Substitute.For<ISeatingSnapshotRepository>();
        exporter = Substitute.For<ISeatingPlanExporter>();
        appSettingsRepo = Substitute.For<IAppSettingsRepository>();
        venueRepo = Substitute.For<IVenueRepository>();
        datasetRepo = Substitute.For<IStudentDatasetRepository>();
        manifestProvider = Substitute.For<StrategyManifestProvider>();
        strategyConfigRepo = Substitute.For<StrategyConfigFileRepository>("/tmp/dummy_config_dir",
            new FileMigrationService([]),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<StrategyConfigFileRepository>>());
        datasetConfigRepo = Substitute.For<StrategyDatasetConfigRepository>("/tmp/dummy_config_dir",
            new FileMigrationService([]),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<StrategyDatasetConfigRepository>>());
        seatSetsService = Substitute.For<ISeatSetsService>();
        logger = Substitute.For<ILogger<ApplicationFacade>>();

        var facade = new ApplicationFacade(
            serviceProvider,
            snapshotRepo,
            new[] { exporter },
            appSettingsRepo,
            venueRepo,
            datasetRepo,
            manifestProvider,
            strategyConfigRepo,
            datasetConfigRepo,
            seatSetsService,
            logger);
        return facade;
    }

    [Fact]
    public async Task ExportSeatingPlanAsync_ShouldCallExporterWithOptions()
    {
        var facade = CreateFacade(out var sp, out var snapRepo, out var exporter,
            out var appRepo, out var venueRepo, out var dr, out var mp, out var scr, out var dcr, out var sss, out var log);
        var ws = new SeatingWorkspace(new List<Student>(), new List<Seat>());
        var options = new ExportOptions { Format = ExportFormat.Excel, Anonymize = true };

        // 设置导出器的 Format 属性
        exporter.Format.Returns(ExportFormat.Excel);

        await facade.ExportSeatingPlanAsync(ws, null, "test.xlsx", options, CancellationToken.None);

        await exporter.Received(1).ExportAsync(
            Arg.Any<SeatingPlan>(),
            "test.xlsx",
            Arg.Is<ExportOptions>(o => o!.Anonymize == true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenameVenueAsync_ShouldLoadSetNameAndSave()
    {
        var facade = CreateFacade(out var sp, out var snapRepo, out var exporter,
            out var appRepo, out var venueRepo, out var dr, out var mp, out var scr, out var dcr, out var sss, out var log);
        var layout = new ClassroomLayoutDefinition { Id = "v1", Name = "旧名" };
        venueRepo.LoadAsync("v1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ClassroomLayoutDefinition?>(layout));

        await facade.RenameVenueAsync("v1", "新名", CancellationToken.None);

        layout.Name.Should().Be("新名");
        await venueRepo.Received(1).SaveAsync("v1", layout, Arg.Any<CancellationToken>());
        _ = sp; _ = snapRepo; _ = exporter; _ = appRepo; _ = dr; _ = mp; _ = scr; _ = dcr; _ = sss; _ = log;
    }

    [Fact]
    public async Task ExportSeatingPlanAsync_TeacherView_ShouldReverseRowsAndColumns()
    {
        var facade = CreateFacade(out var sp, out var snapRepo, out var exporter,
            out var appRepo, out var venueRepo, out var dr, out var mp, out var scr, out var dcr, out var sss, out var log);
        var students = new List<Student>();
        var seats = new List<Seat>
        {
            new GridSeat { Row = 1 , Column = 1 , Id = "s1" } ,
            new GridSeat { Row = 1 , Column = 2 , Id = "s2" } ,
            new GridSeat { Row = 2 , Column = 1 , Id = "s3" }
        };
        var layout = new ClassroomLayoutDefinition
        {
            Name = "测试布局",
            LayoutType = LayoutType.Grid,
            Metadata = new GridLayoutMetadata { Rows = 2, Columns = 2, HasPodium = true },
            Seats = seats
        };
        var ws = new SeatingWorkspace(students, seats);
        var options = new ExportOptions { Format = ExportFormat.Excel, Perspective = LayoutPerspective.TeacherView };

        exporter.Format.Returns(ExportFormat.Excel);

        await facade.ExportSeatingPlanAsync(ws, layout, "test.xlsx", options, CancellationToken.None);

        await exporter.Received(1).ExportLayoutAsync(
            Arg.Is<LayoutSeatingExportModel>(m =>
                // 行反转：讲台移至最后一行
                m!.Rows[m.Rows.Count - 1].Cells.Any(c => c.IsPodium) &&
                // 列镜像：讲台行内 cells 左右颠倒，讲台从中间移至另一侧
                m.Rows[m.Rows.Count - 1].Cells[0].IsPodium),
            "test.xlsx",
            Arg.Is<ExportOptions>(o => o!.Perspective == LayoutPerspective.TeacherView),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportSeatingPlanAsync_ShouldUseTextsFromOptions()
    {
        var facade = CreateFacade(out _, out _, out var exporter,
            out _, out _, out _, out _, out _, out _, out _, out _);
        var seats = new List<Seat> { new GridSeat { Row = 1, Column = 1, Id = "s1" } };
        var layout = new ClassroomLayoutDefinition
        {
            Name = "文案布局",
            LayoutType = LayoutType.Grid,
            Metadata = new GridLayoutMetadata { Rows = 1, Columns = 1, HasPodium = false },
            Seats = seats
        };
        var ws = new SeatingWorkspace(new List<Student>(), seats);
        var options = new ExportOptions
        {
            Format = ExportFormat.Excel,
            Texts = new ExportTexts { Unassigned = "Empty" }
        };
        exporter.Format.Returns(ExportFormat.Excel);

        await facade.ExportSeatingPlanAsync(ws, layout, "test.xlsx", options, CancellationToken.None);

        await exporter.Received(1).ExportLayoutAsync(
            Arg.Is<LayoutSeatingExportModel>(m => m!.Rows[0].Cells[0].Text == "Empty"),
            "test.xlsx",
            Arg.Any<ExportOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteCommandAsync_ShouldDelegateToHistory()
    {
        var facade = CreateFacade(out _, out _, out _,
            out _, out _, out _, out _, out _, out _, out _, out _);
        var cmd = Substitute.For<IUndoableCommand>();
        cmd.ExecuteAsync(Arg.Any<SeatingWorkspace>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var ws = new SeatingWorkspace(new List<Student>(), new List<Seat>());
        typeof(ApplicationFacade)
            .GetField("_currentWorkspace", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(facade, ws);

        var result = await facade.ExecuteCommandAsync(cmd, CancellationToken.None);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task UndoAsync_NoWorkspace_ReturnsFalse()
    {
        var facade = CreateFacade(out _, out _, out _,
            out _, out _, out _, out _, out _, out _, out _, out _);
        var result = await facade.UndoAsync(CancellationToken.None);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RollbackToSnapshot_ShouldApplyAssignments()
    {
        var facade = CreateFacade(out var sp, out var snapRepo, out var exporter,
            out var appRepo, out var venueRepo, out var dr, out var mp, out var scr, out var dcr, out var sss, out var log);

        // 设置会场布局，确保回滚时座位 ID 匹配
        var layout = new ClassroomLayoutDefinition
        {
            Id = "test-venue",
            Name = "Test Venue",
            LayoutType = LayoutType.Grid,
            Seats = [new GridSeat { Id = "seat1" }, new GridSeat { Id = "seat2" }]
        };
        venueRepo.LoadAsync("test-venue", Arg.Any<CancellationToken>()).Returns(layout);

        // 设置数据集仓库返回空（无匹配真实学生，使用存根）
        dr.ListAsync(Arg.Any<CancellationToken>()).Returns([]);

        var snapshot = new SeatingSnapshot
        {
            Id = "snap-1",
            LayoutId = "test-venue",
            SeatAssignments = new Dictionary<string, string> { { "seat1", "s1" }, { "seat2", "s2" } }
        };
        snapRepo.LoadAsync(snapshot.Id, Arg.Any<CancellationToken>()).Returns(snapshot);

        await facade.RollbackToSnapshotAsync(snapshot.Id, CancellationToken.None);

        var workspace = await facade.GetCurrentWorkspaceAsync(TestContext.Current.CancellationToken);
        workspace.Should().NotBeNull();
        workspace!.FindSeats(s => s.Id == "seat1").First().OccupantId.Should().Be("s1");
        workspace.FindSeats(s => s.Id == "seat2").First().OccupantId.Should().Be("s2");
    }

    [Fact]
    public async Task CreateSnapshotAsync_ShouldRecordCurrentDatasetId()
    {
        var facade = CreateFacade(out _, out var snapRepo, out _, out var appRepo,
            out var venueRepo, out var datasetRepo, out _, out _, out _, out _, out _);

        venueRepo.LoadAsync("venue-1", Arg.Any<CancellationToken>()).Returns(new ClassroomLayoutDefinition
        {
            Id = "venue-1",
            Name = "测试教室",
            LayoutType = LayoutType.Grid,
            Seats = [new GridSeat { Id = "seat1" }],
        });
        datasetRepo.LoadAsync("dataset-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<List<Student>?>([new Student { Id = "s1", Name = "张三" }]));
        appRepo.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AppSettings { MaxSnapshotsPerVenue = 0 }));
        venueRepo.GetContentHashAsync("venue-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
        venueRepo.GetRawVenueFileAsync("venue-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));

        await facade.CreateEmptyWorkspaceAsync("venue-1", "dataset-1", TestContext.Current.CancellationToken);
        var snapshot = await facade.CreateSnapshotAsync("手动保存", TestContext.Current.CancellationToken);

        snapshot.Should().NotBeNull();
        snapshot!.DatasetId.Should().Be("dataset-1", "快照应记录创建时的人员数据集 ID");
        await snapRepo.Received(1).SaveAsync(
            Arg.Is<SeatingSnapshot>(s => s.DatasetId == "dataset-1"),
            Arg.Any<CancellationToken>());
    }
}