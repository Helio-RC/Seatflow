using System.Text.Json;
using A_Pair.Core.Models;
using Microsoft.Extensions.Logging;

namespace A_Pair.Infrastructure.Providers
{
    /// <summary>
    /// 策略运行时配置的 per-file JSON 持久化仓储。
    /// 每个策略的配置存储在 <c>{configDir}/{strategyId}.config.json</c>。
    /// </summary>
    /// <param name="configDir">配置目录路径，通常为 AppData/StrategyConfig。</param>
    /// <param name="logger">日志记录器。</param>
    public class StrategyConfigFileRepository (string configDir, ILogger<StrategyConfigFileRepository> logger)
    {
        private readonly string _configDir = configDir ?? throw new ArgumentNullException(nameof(configDir));

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true ,
            WriteIndented = true
        };

        /// <summary>
        /// 加载指定策略的运行时配置。若文件不存在则返回 <c>null</c>。
        /// </summary>
        public async Task<StrategyConfig?> LoadAsync (string strategyId , CancellationToken ct = default)
        {
            var filePath = GetFilePath(strategyId);
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath , ct);
            return JsonSerializer.Deserialize<StrategyConfig>(json , JsonOptions);
        }

        /// <summary>
        /// 保存指定策略的运行时配置。
        /// </summary>
        public async Task SaveAsync (string strategyId , StrategyConfig config , CancellationToken ct = default)
        {
            if (!Directory.Exists(_configDir))
                Directory.CreateDirectory(_configDir);

            var filePath = GetFilePath(strategyId);
            var json = JsonSerializer.Serialize(config , JsonOptions);
            await File.WriteAllTextAsync(filePath , json , ct);
            logger.LogDebug("策略配置已保存：{Id} → {Path}", strategyId, filePath);
        }

        /// <summary>
        /// 加载所有已保存的策略配置。
        /// </summary>
        public async Task<Dictionary<string , StrategyConfig>> LoadAllAsync (CancellationToken ct = default)
        {
            var results = new Dictionary<string , StrategyConfig>();
            if (!Directory.Exists(_configDir))
                return results;

            foreach (var filePath in Directory.EnumerateFiles(_configDir , "*.config.json"))
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                // 去掉 .config 后缀：FixedSeat.config.json → FixedSeat
                var strategyId = Path.GetFileNameWithoutExtension(fileName);

                var json = await File.ReadAllTextAsync(filePath , ct);
                var config = JsonSerializer.Deserialize<StrategyConfig>(json , JsonOptions);
                if (config is not null)
                    results[strategyId] = config;
            }

            return results;
        }

        private string GetFilePath (string strategyId)
            => Path.Combine(_configDir , $"{strategyId}.config.json");
    }
}
