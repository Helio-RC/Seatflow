using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using A_Pair.Core.Models;

namespace A_Pair.Core.Services;

/// <summary>
/// 从嵌入资源加载内置策略的 Manifest，提供不可变的策略元数据。
/// </summary>
public class StrategyManifestProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string ManifestResourcePrefix = "A_Pair.Core.Strategies.Manifests.";

    /// <summary>
    /// 从嵌入资源加载所有内置策略的 Manifest。
    /// </summary>
    public IReadOnlyList<StrategyManifest> GetBuiltInManifests ()
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

    /// <summary>
    /// 根据 ID 获取内置策略的 Manifest。
    /// </summary>
    public StrategyManifest? GetBuiltInManifest (string id)
    {
        return GetBuiltInManifests().FirstOrDefault(m => m.Id == id);
    }
}
