using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SeatFlow.Core.Storage;

namespace SeatFlow.Infrastructure.Storage;

/// <summary>
/// 基于文件系统的 <see cref="ILocalDataStore"/> 实现（桌面端）。
/// 相对路径映射到 <paramref name="rootDirectory"/> 下的真实文件。
/// </summary>
public class FileSystemDataStore(
    string rootDirectory,
    ILogger<FileSystemDataStore>? logger = null) : ILocalDataStore
{
    private readonly string _root = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
    private readonly ILogger<FileSystemDataStore> _logger = logger ?? NullLogger<FileSystemDataStore>.Instance;

    /// <summary>存储根目录（绝对路径），日志与调试用途。</summary>
    public string RootDirectory => _root;

    public async Task<string?> ReadTextAsync(string relativePath, CancellationToken ct = default)
    {
        var path = ToAbsolutePath(relativePath);
        if (!File.Exists(path)) return null;
        try
        {
            return await File.ReadAllTextAsync(path, ct);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "读取文本失败：{Path}", path);
            return null;
        }
    }

    public async Task WriteTextAsync(string relativePath, string content, CancellationToken ct = default)
    {
        var path = ToAbsolutePath(relativePath);
        PrepareParent(path);
        await File.WriteAllTextAsync(path, content, ct);
    }

    public async Task WriteBytesAsync(string relativePath, ReadOnlyMemory<byte> content, CancellationToken ct = default)
    {
        var path = ToAbsolutePath(relativePath);
        PrepareParent(path);
        await File.WriteAllBytesAsync(path, content.ToArray(), ct);
    }

    public async Task<byte[]?> ReadBytesAsync(string relativePath, CancellationToken ct = default)
    {
        var path = ToAbsolutePath(relativePath);
        if (!File.Exists(path)) return null;
        return await File.ReadAllBytesAsync(path, ct);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var path = ToAbsolutePath(relativePath);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
        => Task.FromResult(File.Exists(ToAbsolutePath(relativePath)));

    public Task EnsureDirAsync(string relativeDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ToAbsolutePath(relativeDir));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAsync(string relativeDir, string pattern, CancellationToken ct = default)
    {
        var dir = ToAbsolutePath(relativeDir);
        if (!Directory.Exists(dir)) return Task.FromResult<IReadOnlyList<string>>([]);
        var list = Directory.EnumerateFiles(dir, pattern)
            .Select(ToRelativePath)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(list);
    }

    /// <summary>枚举子目录名（仅末段名称，不与父目录拼接）。</summary>
    public Task<IReadOnlyList<string>> ListDirectoriesAsync(string relativeDir, CancellationToken ct = default)
    {
        var dir = ToAbsolutePath(relativeDir);
        if (!Directory.Exists(dir)) return Task.FromResult<IReadOnlyList<string>>([]);
        var list = Directory.EnumerateDirectories(dir)
            .Select(Path.GetFileName)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList()!;
        return Task.FromResult<IReadOnlyList<string>>(list);
    }

    public Task<DateTime> GetCreationTimeUtcAsync(string relativePath, CancellationToken ct = default)
    {
        var path = ToAbsolutePath(relativePath);
        if (!File.Exists(path)) return Task.FromResult(DateTime.UnixEpoch);
        return Task.FromResult(File.GetCreationTimeUtc(path));
    }

    private static void PrepareParent(string absolutePath)
    {
        var dir = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private string ToAbsolutePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            return relativePath; // 兼容绝对路径调用（测试旧构造）
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_root, normalized);
    }

    private string ToRelativePath(string absolutePath)
        => Path.GetRelativePath(_root, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
}
