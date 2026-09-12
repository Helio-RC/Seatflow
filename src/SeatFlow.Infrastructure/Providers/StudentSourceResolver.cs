using SeatFlow.Core.Storage;

namespace SeatFlow.Infrastructure.Providers;

/// <summary>
/// 学生数据源解析辅助：优先文件系统路径；文件不存在且存在存储抽象时，
/// 回退为存储相对路径（WASM 上传暂存目录 "Uploads/*"）。
/// </summary>
internal static class StudentSourceResolver
{
    /// <summary>
    /// 读取数据源字节。优先文件系统；不存在且提供 store 时走 store。
    /// </summary>
    public static async Task<byte[]?> ReadBytesAsync(string source, ILocalDataStore? store, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(source))
            return null;
        if (File.Exists(source))
            return await File.ReadAllBytesAsync(source, ct);
        if (store is not null)
            return await store.ReadBytesAsync(Normalize(source), ct);
        return null;
    }

    /// <summary>
    /// 规范化为存储相对路径（统一 '/' 分隔，去前缀分隔符）。
    /// </summary>
    public static string Normalize(string source)
        => source.Replace('\\', '/').TrimStart('/');
}
