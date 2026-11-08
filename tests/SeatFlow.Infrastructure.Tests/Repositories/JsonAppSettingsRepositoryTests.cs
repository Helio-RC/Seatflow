using SeatFlow.Infrastructure.Migration;
using SeatFlow.Infrastructure.Migration.Migrators;
namespace SeatFlow.Infrastructure.Tests.Repositories;

public class JsonAppSettingsRepositoryTests
{
    private static string GetTempFilePath() => Path.GetTempFileName() + ".json";

    [Fact]
    public async Task LoadAsync_FileMissing_ShouldReturnDefault()
    {
        var path = GetTempFilePath();
        try
        {
            var repo = new JsonAppSettingsRepository(path, new FileMigrationService([]));
            var settings = await repo.LoadAsync(CancellationToken.None);
            settings.WindowState.Width.Should().Be(1200);
            settings.WindowState.Height.Should().Be(800);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SaveAndLoad_ShouldRoundTrip()
    {
        var path = GetTempFilePath();
        try
        {
            var repo = new JsonAppSettingsRepository(path, new FileMigrationService([]));
            var original = new AppSettings
            {
                LastOpenedFilePath = "/test/file.txt",
                WindowState = new WindowStateSettings
                {
                    Width = 800,
                    Height = 600,
                    IsMaximized = true
                }
            };
            await repo.SaveAsync(original, CancellationToken.None);
            var loaded = await repo.LoadAsync(CancellationToken.None);
            loaded.LastOpenedFilePath.Should().Be("/test/file.txt");
            loaded.WindowState.IsMaximized.Should().BeTrue();
            loaded.WindowState.Width.Should().Be(800);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SettingsFilePath_ShouldReturnCorrectPath()
    {
        var repo = new JsonAppSettingsRepository("C:\\test.json", new FileMigrationService([]));
        repo.SettingsFilePath.Should().Be("C:\\test.json");
    }

    [Fact]
    public async Task LoadAsync_LegacyTelemetryUrl_ShouldMigrateToApiHost()
    {
        var path = GetTempFilePath();
        try
        {
            await File.WriteAllTextAsync(path, """
            {
              "Version": "1.1",
              "Telemetry": { "ServerUrl": "https://seatflow.work/api/app/telemetry", "Enabled": true }
            }
            """, TestContext.Current.CancellationToken);
            var repo = new JsonAppSettingsRepository(path,
                new FileMigrationService([new AppSettingsMigrators.Step_1_0_to_1_1(), new AppSettingsMigrators.Step_1_1_to_1_2()]));

            var settings = await repo.LoadAsync(CancellationToken.None);

            settings.Telemetry.ServerUrl.Should().Be("https://api.seatflow.work/api/app/telemetry");
            settings.Telemetry.Enabled.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_CustomTelemetryUrl_ShouldBePreserved()
    {
        var path = GetTempFilePath();
        try
        {
            await File.WriteAllTextAsync(path, """
            { "Version": "1.1", "Telemetry": { "ServerUrl": "https://example.com/collect" } }
            """, TestContext.Current.CancellationToken);
            var repo = new JsonAppSettingsRepository(path,
                new FileMigrationService([new AppSettingsMigrators.Step_1_0_to_1_1(), new AppSettingsMigrators.Step_1_1_to_1_2()]));

            var settings = await repo.LoadAsync(CancellationToken.None);

            settings.Telemetry.ServerUrl.Should().Be("https://example.com/collect");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_NewerFileVersion_ShouldStillLoad()
    {
        // 老版本客户端读到 1.2 文件不应报错（FileMigrationService 只向前迁移）
        var path = GetTempFilePath();
        try
        {
            await File.WriteAllTextAsync(path, """
            { "Version": "1.2", "Telemetry": { "ServerUrl": "https://example.com/collect" } }
            """, TestContext.Current.CancellationToken);
            var repo = new JsonAppSettingsRepository(path,
                new FileMigrationService([new AppSettingsMigrators.Step_1_0_to_1_1(), new AppSettingsMigrators.Step_1_1_to_1_2()]));

            var settings = await repo.LoadAsync(CancellationToken.None);

            settings.Telemetry.ServerUrl.Should().Be("https://example.com/collect");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}