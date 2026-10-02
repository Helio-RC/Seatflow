using SeatFlow.Infrastructure.Migration;

namespace SeatFlow.Infrastructure.Tests.Providers;

/// <summary>
/// 会场摘要轻量读取测试：仅解析 JSON 取名称，不反序列化座位；损坏文件回退为 ID。
/// </summary>
public class JsonVenueRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "seatflow-venue-repo-" + Guid.NewGuid().ToString("N"));

    private JsonVenueRepository CreateRepo()
    {
        Directory.CreateDirectory(_dir);
        return new JsonVenueRepository(_dir, new FileMigrationService([]));
    }

    [Fact]
    public async Task ListVenueSummariesAsync_ShouldReturnNameFromLayout()
    {
        var repo = CreateRepo();
        await repo.SaveAsync(
            "v1",
            new ClassroomLayoutDefinition { Id = "v1", Name = "一号教室" },
            TestContext.Current.CancellationToken);

        var summaries = await repo.ListVenueSummariesAsync(TestContext.Current.CancellationToken);

        summaries.Should().ContainSingle();
        summaries[0].Id.Should().Be("v1");
        summaries[0].Name.Should().Be("一号教室");
    }

    [Fact]
    public async Task ListVenueSummariesAsync_InvalidJson_ShouldFallbackToId()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(
            Path.Combine(_dir, "bad.venue.json"),
            "{ not-json",
            TestContext.Current.CancellationToken);
        var repo = CreateRepo();

        var summaries = await repo.ListVenueSummariesAsync(TestContext.Current.CancellationToken);

        summaries.Should().ContainSingle();
        summaries[0].Id.Should().Be("bad");
        summaries[0].Name.Should().Be("bad", "损坏文件回退为 ID，列表仍可用");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 清理失败不影响测试 */ }
    }
}
