namespace SeatFlow.Core.Storage;

/// <summary>
/// 本地数据存储抽象。桌面端由文件系统实现（<c>AppData</c> 目录），
/// 浏览器端（WASM）由 IndexedDB 实现。所有路径均为使用 <c>/</c> 分隔的
/// <b>相对路径</b>，不含盘符或根（如 <c>Venues/abc.venue.json</c>）。
/// 存储实现负责将相对路径映射到底层介质（文件系统子目录 / IndexedDB key）。
/// </summary>
/// <remarks>
/// 所有操作均为异步：IndexedDB 只有异步 JS API 可调用；文件系统实现
/// 内部使用 <see cref="System.IO.File"/> 的异步 API，保持统一语义。
/// </remarks>
public interface ILocalDataStore
{
    /// <summary>
    /// 读取文本内容；路径不存在时返回 <c>null</c>。
    /// </summary>
    Task<string?> ReadTextAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// 写入文本内容（覆盖语义），必要时补建父目录。
    /// </summary>
    Task WriteTextAsync(string relativePath, string content, CancellationToken ct = default);

    /// <summary>
    /// 写入字节内容（覆盖语义），必要时补建父目录。
    /// </summary>
    Task WriteBytesAsync(string relativePath, ReadOnlyMemory<byte> content, CancellationToken ct = default);

    /// <summary>
    /// 读取字节内容；路径不存在时返回 <c>null</c>。
    /// </summary>
    Task<byte[]?> ReadBytesAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// 删除文件；文件不存在时静默成功。
    /// </summary>
    Task DeleteAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// 路径（文件）是否存在。
    /// </summary>
    Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// 确保目录存在（IndexedDB 实现为 no-op，无目录概念）。
    /// </summary>
    Task EnsureDirAsync(string relativeDir, CancellationToken ct = default);

    /// <summary>
    /// 枚举目录下匹配 glob 模式（如 <c>*.venue.json</c>）的<b>相对路径</b>列表。
    /// 目录不存在时返回空列表。
    /// </summary>
    Task<IReadOnlyList<string>> ListAsync(string relativeDir, string pattern, CancellationToken ct = default);

    /// <summary>
    /// 枚举目录下的子目录名（相对路径），目录不存在时返回空列表。
    /// </summary>
    Task<IReadOnlyList<string>> ListDirectoriesAsync(string relativeDir, CancellationToken ct = default);

    /// <summary>
    /// 获取文件的创建时间（UTC）。文件不存在时返回 <see cref="DateTime.UnixEpoch"/>。
    /// </summary>
    Task<DateTime> GetCreationTimeUtcAsync(string relativePath, CancellationToken ct = default);
}
