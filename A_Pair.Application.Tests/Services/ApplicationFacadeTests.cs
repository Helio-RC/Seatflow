using A_Pair.Core.Exporters;
using A_Pair.Core.Providers;
using A_Pair.Core.Services;
using A_Pair.Infrastructure.Providers;
using Microsoft.Extensions.Logging;

namespace A_Pair.Application.Tests.Services;

public class ApplicationFacadeTests
{
    private static ApplicationFacade CreateFacade (
        out IServiceProvider serviceProvider ,
        out ISeatingSnapshotRepository snapshotRepo ,
        out ISeatingPlanExporter exporter ,
        out IPluginManager pluginManager ,
        out IPluginConfigurationService pluginConfigService ,
        out IAppSettingsRepository appSettingsRepo ,
        out IVenueRepository venueRepo ,
        out IStudentDatasetRepository datasetRepo ,
        out StrategyManifestProvider manifestProvider ,
        out StrategyConfigFileRepository strategyConfigRepo ,
        out ILogger<ApplicationFacade> logger)
    {
        serviceProvider = Substitute.For<IServiceProvider>();
        snapshotRepo = Substitute.For<ISeatingSnapshotRepository>();
        exporter = Substitute.For<ISeatingPlanExporter>();
        pluginManager = Substitute.For<IPluginManager>();
        pluginConfigService = Substitute.For<IPluginConfigurationService>();
        appSettingsRepo = Substitute.For<IAppSettingsRepository>();
        venueRepo = Substitute.For<IVenueRepository>();
        datasetRepo = Substitute.For<IStudentDatasetRepository>();
        manifestProvider = Substitute.For<StrategyManifestProvider>();
        strategyConfigRepo = Substitute.For<StrategyConfigFileRepository>("/tmp/dummy_config_dir" ,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<StrategyConfigFileRepository>>());
        logger = Substitute.For<ILogger<ApplicationFacade>>();

        var facade = new ApplicationFacade(
            serviceProvider ,
            snapshotRepo ,
            new[] { exporter } ,
            pluginManager ,
            pluginConfigService ,
            appSettingsRepo ,
            venueRepo ,
            datasetRepo ,
            manifestProvider ,
            strategyConfigRepo ,
            logger);
        return facade;
    }

    [Fact]
    public async Task ExportSeatingPlanAsync_ShouldCallExporterWithOptions ()
    {
        var facade = CreateFacade(out var sp , out var snapRepo , out var exporter ,
            out var pm , out var pcs , out var appRepo , out var venueRepo , out var dr , out var mp , out var scr , out var log);
        var ws = new SeatingWorkspace(new List<Student>() , new List<Seat>());
        var options = new ExportOptions { Format = ExportFormat.Excel , Anonymize = true };

        // 设置导出器的 Format 属性
        exporter.Format.Returns(ExportFormat.Excel);

        await facade.ExportSeatingPlanAsync(ws , null , "test.xlsx" , options , CancellationToken.None);

        await exporter.Received(1).ExportAsync(
            Arg.Any<SeatingPlan>() ,
            "test.xlsx" ,
            Arg.Is<ExportOptions>(o => o.Anonymize == true) ,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteCommandAsync_ShouldDelegateToHistory ()
    {
        var facade = CreateFacade(out var sp , out var snapRepo , out var exporter ,
            out var pm , out var pcs , out var appRepo , out var venueRepo , out var dr , out var mp , out var scr , out var log);
        var cmd = Substitute.For<IUndoableCommand>();
        cmd.ExecuteAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var ws = new SeatingWorkspace(new List<Student>() , new List<Seat>());
        typeof(ApplicationFacade)
            .GetField("_currentWorkspace" , System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(facade , ws);

        var result = await facade.ExecuteCommandAsync(cmd , CancellationToken.None);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task UndoAsync_NoWorkspace_ReturnsFalse ()
    {
        var facade = CreateFacade(out var sp , out var snapRepo , out var exporter ,
            out var pm , out var pcs , out var appRepo , out var venueRepo , out var dr , out var mp , out var scr , out var log);
        var result = await facade.UndoAsync(CancellationToken.None);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RollbackToSnapshot_ShouldApplyAssignments ()
    {
        var facade = CreateFacade(out var sp , out var snapRepo , out var exporter ,
            out var pm , out var pcs , out var appRepo , out var venueRepo , out var dr , out var mp , out var scr , out var log);

        // 设置会场布局，确保回滚时座位 ID 匹配
        var layout = new ClassroomLayoutDefinition
        {
            Id = "test-venue" ,
            Name = "Test Venue" ,
            LayoutType = LayoutType.Grid ,
            Seats = [new GridSeat { Id = "seat1" } , new GridSeat { Id = "seat2" }]
        };
        venueRepo.LoadAsync("test-venue" , Arg.Any<CancellationToken>()).Returns(layout);

        // 设置数据集仓库返回空（无匹配真实学生，使用存根）
        dr.ListAsync(Arg.Any<CancellationToken>()).Returns([]);

        var snapshot = new SeatingSnapshot
        {
            Id = "snap-1" ,
            LayoutId = "test-venue" ,
            SeatAssignments = new Dictionary<string , string> { { "seat1" , "s1" } , { "seat2" , "s2" } }
        };
        snapRepo.LoadAsync(snapshot.Id , Arg.Any<CancellationToken>()).Returns(snapshot);

        await facade.RollbackToSnapshotAsync(snapshot.Id , CancellationToken.None);

        var workspace = await facade.GetCurrentWorkspaceAsync(TestContext.Current.CancellationToken);
        workspace.Should().NotBeNull();
        workspace!.FindSeats(s => s.Id == "seat1").First().OccupantId.Should().Be("s1");
        workspace.FindSeats(s => s.Id == "seat2").First().OccupantId.Should().Be("s2");
    }
}