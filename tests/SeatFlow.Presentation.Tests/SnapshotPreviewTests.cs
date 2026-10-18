using System.Text.Json;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Infrastructure.Layouts;
using SeatFlow.Infrastructure.Serialization;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>快照预览与会场页/工作台共用同一 Grid 视觉几何（桌间距、讲台、门）。</summary>
public class SnapshotPreviewTests
{
    private static readonly JsonSerializerOptions VenueJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new SeatJsonConverter() },
    };

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var started = Environment.TickCount64;
        while (!condition() && Environment.TickCount64 - started < timeoutMs)
            await Task.Delay(15);

        if (!condition())
            throw new TimeoutException($"等待条件超时（{timeoutMs}ms）");
    }

    private static (GridLayoutMetadata Meta, ClassroomLayoutDefinition Layout) CreateGridLayout()
    {
        var meta = new GridLayoutMetadata
        {
            Rows = 3,
            Columns = 4,
            HorizontalSpacing = VenueLayoutDefaults.GridHorizontalSpacing,
            VerticalSpacing = VenueLayoutDefaults.GridVerticalSpacing,
            OriginX = VenueLayoutDefaults.GridOriginX,
            OriginY = VenueLayoutDefaults.GridOriginY,
            SeatsPerDesk = VenueLayoutDefaults.GridSeatsPerDesk,
            IntraDeskSpacing = VenueLayoutDefaults.GridIntraDeskSpacing,
            InterDeskSpacing = VenueLayoutDefaults.GridInterDeskSpacing,
            AisleWidth = VenueLayoutDefaults.GridAisleWidth,
            FrontRowCount = VenueLayoutDefaults.GridFrontRowCount,
            HasPodium = VenueLayoutDefaults.GridHasPodium,
            PodiumWidth = VenueLayoutDefaults.GridPodiumWidth,
            PodiumHeight = VenueLayoutDefaults.GridPodiumHeight,
        };
        var layout = GridLayoutBuilder.BuildGrid(meta);
        layout.Id = "v1";
        layout.Name = "测试教室";

        // 与会场页保存一致：讲台居中于原始网格，门为固定尺寸
        double gridLeft = layout.Seats.OfType<GridSeat>()
            .Min(s => SeatFlow.Core.DomainServices.SeatGeometryHelper.GetPosition(s, meta).X);
        double gridRight = layout.Seats.OfType<GridSeat>()
            .Max(s => SeatFlow.Core.DomainServices.SeatGeometryHelper.GetPosition(s, meta).X);
        double podiumW = meta.PodiumWidth;
        layout.Obstacles.Add(new Obstacle
        {
            X = ((gridLeft + gridRight) / 2) - (podiumW / 2),
            Y = meta.OriginY - meta.PodiumHeight - meta.VerticalSpacing,
            Width = podiumW,
            Height = meta.PodiumHeight,
            Type = "Podium",
        });
        layout.Obstacles.Add(new Obstacle { X = 100, Y = 100, Width = 36, Height = 24, Type = "Door" });
        return (meta, layout);
    }

    [AvaloniaFact]
    public async Task 快照预览_Grid几何与会场页和工作台一致()
    {
        var (meta, layout) = CreateGridLayout();
        var venueJson = JsonSerializer.Serialize(
            new VenueFile { Version = "1.1", VenueId = "v1", Layout = layout }, VenueJsonOptions);
        var snapshot = new SeatingSnapshot
        {
            Id = "s1",
            LayoutId = "v1",
            DatasetId = "d1",
            Metadata = new Dictionary<string, object> { ["venueFile"] = venueJson },
        };

        var facade = Substitute.For<IApplicationFacade>();
        facade.LoadAppSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AppSettings()));
        facade.ListVenueIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<string>>(["v1"]));
        facade.LoadVenueAsync("v1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ClassroomLayoutDefinition?>(layout));
        facade.ListStudentDatasetsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StudentDatasetInfo>>([]));
        facade.GetSnapshotsAsync("v1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SeatingSnapshot>>([snapshot]));

        var vm = new SnapshotHistoryViewModel(
            facade,
            Substitute.For<INavigationService>(),
            new ShellLayoutService(),
            NullDialogService.Instance);

        await vm.OnEnterAsync(CancellationToken.None);
        vm.SelectedVenue = vm.Venues[0];
        await WaitUntilAsync(() => vm.SelectedSnapshot is not null && vm.PreviewSnapshot is not null);

        var preview = vm.PreviewSnapshot!;
        var geometry = GridVisualGeometryBuilder.Build(meta);
        var g11 = geometry.Seats.Single(s => s.Row == 1 && s.Column == 1);
        var g12 = geometry.Seats.Single(s => s.Row == 1 && s.Column == 2);
        var g13 = geometry.Seats.Single(s => s.Row == 1 && s.Column == 3);
        var p11 = preview.Seats.Single(s => s.SeatLabel == "R1C1");
        var p12 = preview.Seats.Single(s => s.SeatLabel == "R1C2");
        var p13 = preview.Seats.Single(s => s.SeatLabel == "R1C3");

        (p12.X - p11.X).Should().BeApproximately(g12.X - g11.X, 0.01,
            "同桌内间距应与会场页/工作台一样按 0.8 视觉压缩");
        (p13.X - p12.X).Should().BeApproximately(g13.X - g12.X, 0.01,
            "桌间距应与会场页/工作台一致");
        p11.Y.Should().BeApproximately(g11.Y + (40 - geometry.Seats.Min(s => s.Y)), 0.01,
            "快照使用与工作台相同的 40px 内边距");

        var podium = preview.Overlays!.Single(o => o.IsRound);
        podium.Width.Should().BeApproximately(meta.PodiumWidth * geometry.FactorX, 0.01);
        var door = preview.Overlays!.Single(o => o.IsDoor);
        door.Width.Should().Be(36, "门保持固定尺寸，不随视觉放大系数缩放");
        door.Height.Should().Be(24);
    }
}
