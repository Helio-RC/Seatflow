using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Infrastructure.Tests.Repositories;

public class StrategyConfigFileRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StrategyConfigFileRepository _repo;

    public StrategyConfigFileRepositoryTests ()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _repo = new StrategyConfigFileRepository(_tempDir, NullLogger<StrategyConfigFileRepository>.Instance);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsNull ()
    {
        var result = await _repo.LoadAsync("NonExistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip ()
    {
        var config = new StrategyConfig { Priority = 50, IsEnabled = true };
        config.Parameters["key"] = "value";

        await _repo.SaveAsync("TestStrategy", config);
        var loaded = await _repo.LoadAsync("TestStrategy");

        loaded.Should().NotBeNull();
        loaded!.Priority.Should().Be(50);
        loaded.IsEnabled.Should().BeTrue();
        loaded.Parameters["key"]!.ToString().Should().Be("value");
    }

    [Fact]
    public async Task LoadAllAsync_EmptyDir_ReturnsEmpty ()
    {
        var result = await _repo.LoadAllAsync();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAllAsync_WithMultipleFiles_ReturnsAll ()
    {
        await _repo.SaveAsync("A", new StrategyConfig { Priority = 10 });
        await _repo.SaveAsync("B", new StrategyConfig { Priority = 20 });

        var result = await _repo.LoadAllAsync();
        result.Should().HaveCount(2);
        result.Should().ContainKeys("A", "B");
    }

    [Fact]
    public async Task SaveAsync_Overwrites ()
    {
        await _repo.SaveAsync("X", new StrategyConfig { Priority = 1 });
        await _repo.SaveAsync("X", new StrategyConfig { Priority = 99 });

        var loaded = await _repo.LoadAsync("X");
        loaded!.Priority.Should().Be(99);
    }

    public void Dispose ()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
