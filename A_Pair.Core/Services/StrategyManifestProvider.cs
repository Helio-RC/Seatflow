using System.Text.Json;
using A_Pair.Core.Models;

namespace A_Pair.Core.Services;

/// <summary>
/// 从嵌入资源加载内置策略的 Manifest，提供不可变的策略元数据。
/// 首次读取后缓存结果，避免重复的嵌入资源 I/O。
/// </summary>
public class StrategyManifestProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string ManifestResourcePrefix = "A_Pair.Core.Strategies.Manifests.";

    private readonly Lazy<IReadOnlyList<StrategyManifest>> _manifests;

    public StrategyManifestProvider()
    {
        _manifests = new Lazy<IReadOnlyList<StrategyManifest>>(LoadBuiltInManifests);
    }

    /// <summary>
    /// 从嵌入资源加载所有内置策略的 Manifest（首次调用时加载，后续返回缓存）。
    /// </summary>
    public IReadOnlyList<StrategyManifest> GetBuiltInManifests()
    {
        return _manifests.Value;
    }

    /// <summary>
    /// 根据 ID 获取内置策略的 Manifest。
    /// </summary>
    public StrategyManifest? GetBuiltInManifest(string id)
    {
        return _manifests.Value.FirstOrDefault(m => m.Id == id);
    }

    private IReadOnlyList<StrategyManifest> LoadBuiltInManifests()
    {
        var assembly = typeof(StrategyManifestProvider).Assembly;
        var results = new List<StrategyManifest>();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ManifestResourcePrefix) || !resourceName.EndsWith(".json"))
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) continue;

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var manifest = JsonSerializer.Deserialize<StrategyManifest>(json, JsonOptions);
            if (manifest is not null)
                results.Add(manifest);
        }

        return results.OrderBy(m => m.DefaultPriority).ToList().AsReadOnly();
    }
}
