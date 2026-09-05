#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SeatFlow.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Storage;

/// <summary>
/// 基于 IndexedDB 的 <see cref="ILocalDataStore"/> 实现（WASM/浏览器端）。
/// 相对路径即 IndexedDB key（如 <c>Venues/abc.venue.json</c>）。
/// 每个文件以 <c>{ path: string, data: Uint8Array }</c> 记录存储于单一 object store
/// <c>files</c> 中（由宿主 interop.js 建立）。IndexedDB 无目录概念，目录操作均为 no-op。
/// </summary>
/// <remarks>
/// JS 桥接模块名固定为 <c>"sf.idb"</c>，宿主（SeatFlow.Browser）启动时通过
/// <c>JSHost.ImportAsync("sf.idb" , "js/interop.js")</c> 加载。
/// 源生成 interop 对 Task 泛型返回限制严格（仅支持 string/bool/void 等简单类型），
/// 因此复合值（字节、字符串列表）以 JSON/base64 字符串为传输载体。
/// </remarks>
[SupportedOSPlatform("browser")]
public partial class IndexedDbDataStore (
    ILogger<IndexedDbDataStore>? logger = null) : ILocalDataStore
{
    private readonly ILogger<IndexedDbDataStore> _logger = logger ?? NullLogger<IndexedDbDataStore>.Instance;

    #region JS imports（async：IndexedDB 无同步 API；载体：string/base64/JSON）

    /// <summary>写入 key 对应值（内容以 base64 传输，JS 侧解码为 Uint8Array 存 IndexedDB）。</summary>
    [JSImport("idbWrite" , "sf.idb")]
    internal static partial Task ImportWrite (string key , string base64Content);

    /// <summary>读取 key；不存在返回 null（JS 将存储字节解码为 base64 返回）。</summary>
    [JSImport("idbRead" , "sf.idb")]
    internal static partial Task<string?> ImportRead (string key);

    [JSImport("idbDelete" , "sf.idb")]
    internal static partial Task ImportDelete (string key);

    [JSImport("idbExists" , "sf.idb")]
    internal static partial Task<bool> ImportExists (string key);

    /// <summary>枚举所有 key（JSON 数组字符串），调用方过滤前缀。</summary>
    [JSImport("idbListKeys" , "sf.idb")]
    internal static partial Task<string?> ImportListKeys ();

    [JSImport("idbCreationTimeUtc" , "sf.idb")]
    internal static partial Task<string?> ImportCreationTimeUtc (string key);

    #endregion

    public async Task<string?> ReadTextAsync (string relativePath , CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rawB64 = await ImportRead(relativePath);
        if (rawB64 is null) return null;
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(rawB64));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public async Task WriteTextAsync (string relativePath , string content , CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await ImportWrite(relativePath , Convert.ToBase64String(Encoding.UTF8.GetBytes(content)));
    }

    public async Task WriteBytesAsync (string relativePath , ReadOnlyMemory<byte> content , CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await ImportWrite(relativePath , Convert.ToBase64String(content.Span));
    }

    public async Task<byte[]?> ReadBytesAsync (string relativePath , CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rawB64 = await ImportRead(relativePath);
        return rawB64 is null ? null : Convert.FromBase64String(rawB64);
    }

    public async Task DeleteAsync (string relativePath , CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await ImportDelete(relativePath);
    }

    public async Task<bool> ExistsAsync (string relativePath , CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await ImportExists(relativePath);
    }

    public Task EnsureDirAsync (string relativeDir , CancellationToken ct = default)
        => Task.CompletedTask;

    public async Task<IReadOnlyList<string>> ListAsync (string relativeDir , string pattern , CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var prefix = string.IsNullOrEmpty(relativeDir) ? "" : relativeDir.TrimEnd('/') + "/";
        var regex = GlobToRegex(pattern);
        var keys = await ImportListKeys();
        var all = keys is null
            ? []
            : JsonSerializer.Deserialize<List<string>>(keys) ?? [];
        return all
            .Where(k => k.StartsWith(prefix , StringComparison.Ordinal))
            .Where(k => regex.IsMatch(k.Substring(prefix.Length)))
            .OrderBy(x => x , StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> ListDirectoriesAsync (string relativeDir , CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var prefix = string.IsNullOrEmpty(relativeDir) ? "" : relativeDir.TrimEnd('/') + "/";
        var keys = await ImportListKeys();
        var all = keys is null
            ? []
            : JsonSerializer.Deserialize<List<string>>(keys) ?? [];
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in all)
        {
            if (!key.StartsWith(prefix , StringComparison.Ordinal)) continue;
            var rest = key.Substring(prefix.Length);
            var idx = rest.IndexOf('/');
            if (idx > 0)
                set.Add(rest[..idx]); // 仅返回目录名（末段），与 FileSystemDataStore 语义一致
        }
        return set.ToList();
    }

    public async Task<DateTime> GetCreationTimeUtcAsync (string relativePath , CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var iso = await ImportCreationTimeUtc(relativePath);
        if (iso is null) return DateTime.UnixEpoch;
        return DateTime.Parse(iso , null , System.Globalization.DateTimeStyles.AdjustToUniversal);
    }

    private static Regex GlobToRegex (string pattern)
    {
        var rx = new StringBuilder("^");
        foreach (var ch in pattern)
        {
            switch (ch)
            {
                case '*': rx.Append(".*"); break;
                case '?': rx.Append('.'); break;
                default: rx.Append(Regex.Escape(ch.ToString())); break;
            }
        }
        rx.Append('$');
        return new Regex(rx.ToString() , RegexOptions.Compiled);
    }
}
#endif
