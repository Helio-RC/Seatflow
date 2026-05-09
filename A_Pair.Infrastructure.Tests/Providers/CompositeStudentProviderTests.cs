namespace A_Pair.Infrastructure.Tests.Providers;

public class CompositeStudentProviderTests : IDisposable
{
    private readonly string _tempDir;

    public CompositeStudentProviderTests ()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task LoadAsync_EmptySource_ReturnsEmptyList ()
    {
        var provider = new CompositeStudentProvider();
        var result = await provider.LoadAsync("");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_NonExistentFile_ReturnsEmptyList ()
    {
        var provider = new CompositeStudentProvider();
        var result = await provider.LoadAsync("/nonexistent/file.csv");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_UnknownExtension_ReturnsEmptyList ()
    {
        var path = Path.Combine(_tempDir, "test.xyz");
        await File.WriteAllTextAsync(path, "data");
        var provider = new CompositeStudentProvider();
        var result = await provider.LoadAsync(path);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_ValidCsv_ReturnsStudents ()
    {
        var path = Path.Combine(_tempDir, "students.csv");
        await File.WriteAllTextAsync(path, "姓名\n\n张三\n李四");

        var provider = new CompositeStudentProvider();
        var result = await provider.LoadAsync(path);
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("张三");
        result[1].Name.Should().Be("李四");
    }

    [Fact]
    public async Task LoadAsync_ValidJson_ReturnsStudents ()
    {
        var path = Path.Combine(_tempDir, "students.json");
        var json = """{"version":"1.0","students":[{"id":"1","name":"Alice"},{"id":"2","name":"Bob"}]}""";
        await File.WriteAllTextAsync(path, json);

        var provider = new CompositeStudentProvider();
        var result = await provider.LoadAsync(path);
        result.Should().HaveCount(2);
        result[0].Name.Should().BeOneOf("Alice", "Bob");
    }

    public void Dispose ()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
