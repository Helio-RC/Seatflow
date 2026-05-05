using System.Text.Json;
using System.Text.Json.Serialization;
using A_Pair.Core.Models;

namespace A_Pair.Infrastructure.Providers
{
    /// <summary>
    /// JSON 格式的策略配置仓储，将策略配置（优先级、启用状态、参数）持久化到本地文件。
    /// </summary>
    /// <remarks>
    /// 如果配置文件不存在，<see cref="LoadAllAsync"/> 返回空字典。
    /// 保存时自动创建目录结构。
    /// </remarks>
    /// <param name="filePath">配置文件的完整路径。</param>
    public class JsonStrategyConfigRepository (string filePath)
    {
        private readonly string _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// 加载所有已保存的策略配置，按策略 ID 索引。
        /// </summary>
        public async Task<Dictionary<string, StrategyConfigDto>> LoadAllAsync (CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_filePath))
                return [];

            var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
            var list = JsonSerializer.Deserialize<List<StrategyConfigDto>>(json, JsonOptions);
            return list?.ToDictionary(s => s.Id) ?? [];
        }

        /// <summary>
        /// 批量保存策略配置。
        /// </summary>
        public async Task SaveAllAsync (List<StrategyConfigDto> configs , CancellationToken cancellationToken = default)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(configs, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, cancellationToken);
        }
    }
}
