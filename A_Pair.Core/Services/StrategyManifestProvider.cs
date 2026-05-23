using System.Text.Json;
using A_Pair.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly ILogger<StrategyManifestProvider> _logger;

    public StrategyManifestProvider(ILogger<StrategyManifestProvider>? logger = null)
    {
        _logger = logger ?? NullLogger<StrategyManifestProvider>.Instance;
        _manifests = new Lazy<IReadOnlyList<StrategyManifest>>(LoadBuiltInManifests);
    }

    // 无参构造函数供 NSubstitute 等动态代理框架使用
    public StrategyManifestProvider() : this(null) { }

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
        var failed = 0;

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ManifestResourcePrefix) || !resourceName.EndsWith(".json"))
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                _logger.LogWarning("无法读取嵌入资源：{ResourceName}", resourceName);
                failed++;
                continue;
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            try
            {
                var manifest = JsonSerializer.Deserialize<StrategyManifest>(json, JsonOptions);
                if (manifest is not null)
                    results.Add(manifest);
                else
                {
                    _logger.LogWarning("Manifest 反序列化结果为 null：{ResourceName}", resourceName);
                    failed++;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Manifest JSON 反序列化失败：{ResourceName}", resourceName);
                failed++;
            }
        }

        _logger.LogInformation("加载内置策略清单：成功 {Success} 个，失败 {Failed} 个", results.Count, failed);
        return results.OrderBy(m => m.DefaultPriority).ToList().AsReadOnly();
    }
}
