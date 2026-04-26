using A_Pair.Core.Exporters;
using A_Pair.Core.Providers;

namespace A_Pair.Application.Tests.Services;

public class ApplicationFacadeTests
{
    private static ApplicationFacade CreateFacade (
        out IServiceProvider serviceProvider ,
        out ISeatingSnapshotRepository snapshotRepo ,
        out ISeatingPlanExporter exporter ,
        out PluginManager pluginManager ,
        out IPluginConfigurationService pluginConfigService ,
        out IAppSettingsRepository appSettingsRepo ,
        out IVenueRepository venueRepo)
    {
        serviceProvider = Substitute.For<IServiceProvider>();
        snapshotRepo = Substitute.For<ISeatingSnapshotRepository>();
        exporter = Substitute.For<ISeatingPlanExporter>();
        pluginManager = Substitute.For<PluginManager>("dummyPath");
        pluginConfigService = Substitute.For<IPluginConfigurationService>();
        appSettingsRepo = Substitute.For<IAppSettingsRepository>();
        venueRepo = Substitute.For<IVenueRepository>();

        var facade = new ApplicationFacade(
            serviceProvider ,
            snapshotRepo ,
            new[] { exporter } ,
            pluginManager ,
            pluginConfigService ,
            appSettingsRepo ,
            venueRepo);
        return facade;
    }

    [Fact]
    public async Task ExportSeatingPlanAsync_ShouldCallExporterWithOptions ()
    {
        var facade = CreateFacade(out var sp , out var snapRepo , out var exporter ,
            out var pm , out var pcs , out var appRepo , out var venueRepo);
        var ws = new SeatingWorkspace(new List<Student>() , new List<Seat>());
        var options = new ExportOptions { Format = ExportFormat.Excel , Anonymize = true };

        // 设置导出器的 Format 属性
        exporter.Format.Returns(ExportFormat.Excel);

        await facade.ExportSeatingPlanAsync(ws , "test.xlsx" , options , CancellationToken.None);

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
            out var pm , out var pcs , out var appRepo , out var venueRepo);
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
            out var pm , out var pcs , out var appRepo , out var venueRepo);
        var result = await facade.UndoAsync(CancellationToken.None);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RollbackToSnapshot_ShouldApplyAssignments ()
    {
        var facade = CreateFacade(out var sp , out var snapRepo , out var exporter ,
            out var pm , out var pcs , out var appRepo , out var venueRepo);
        var ws = new SeatingWorkspace(
            new[] { new Student { Id = "s1" } , new Student { Id = "s2" } } ,
            new Seat[] { new GridSeat { Id = "seat1" } , new GridSeat { Id = "seat2" } });
        typeof(ApplicationFacade)
            .GetField("_currentWorkspace" , System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(facade , ws);

        var snapshot = new SeatingSnapshot
        {
            SeatAssignments = new Dictionary<string , string> { { "seat1" , "s1" } , { "seat2" , "s2" } }
        };
        snapRepo.Load(snapshot.Id).Returns(snapshot);

        await facade.RollbackToSnapshotAsync(snapshot.Id , CancellationToken.None);

        ws.FindSeats(s => s.Id == "seat1").First().OccupantId.Should().Be("s1");
        ws.FindSeats(s => s.Id == "seat2").First().OccupantId.Should().Be("s2");
    }
}