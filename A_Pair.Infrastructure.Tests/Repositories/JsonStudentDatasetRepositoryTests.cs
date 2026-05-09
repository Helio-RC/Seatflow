namespace A_Pair.Infrastructure.Tests.Repositories;

public class JsonStudentDatasetRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonStudentDatasetRepository _repo;

    public JsonStudentDatasetRepositoryTests ()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _repo = new JsonStudentDatasetRepository(_tempDir);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip ()
    {
        var students = new List<Student>
        {
            new() { Id = "1", Name = "Alice" },
            new() { Id = "2", Name = "Bob" }
        };

        const string id = "test";
        await _repo.SaveAsync(id, "test", students, "original.xlsx");
        var loaded = await _repo.LoadAsync(id);

        loaded.Should().NotBeNull();
        loaded!.Should().HaveCount(2);
        loaded[0].Name.Should().Be("Alice");
    }

    [Fact]
    public async Task LoadAsync_InvalidId_ReturnsNull ()
    {
        var result = await _repo.LoadAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_EmptyDir_ReturnsEmpty ()
    {
        var result = await _repo.ListAsync();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_WithSavedDataset_ReturnsInfo ()
    {
        const string id = "mydataset";
        await _repo.SaveAsync(id, "mydataset", new List<Student> { new() { Id = "1", Name = "X" } }, null);
        var list = await _repo.ListAsync();
        list.Should().HaveCount(1);
        list[0].Id.Should().Be(id);
        list[0].Name.Should().Be("mydataset");
        list[0].StudentCount.Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_RemovesDataset ()
    {
        const string id = "temp";
        await _repo.SaveAsync(id, "temp", new List<Student>(), null);
        await _repo.DeleteAsync(id);
        var result = await _repo.LoadAsync(id);
        result.Should().BeNull();
    }

    public void Dispose ()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
