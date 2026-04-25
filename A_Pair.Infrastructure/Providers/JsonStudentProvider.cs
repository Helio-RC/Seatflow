using System.Text.Json;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;

namespace A_Pair.Infrastructure.Providers
{
    /// <summary>
    /// JSON 格式的学生数据提供器，从 JSON 文件反序列化学生列表。
    /// </summary>
    /// <remarks>
    /// 期望 JSON 文件包含一个 <see cref="Student"/> 对象的数组。
    /// 如果文件路径为空或文件不存在，则返回空列表。
    /// </remarks>
    public class JsonStudentProvider : IStudentProvider
    {
        /// <inheritdoc />
        public async Task<List<Student>> LoadAsync (string source , CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(source) || !File.Exists(source)) return new List<Student>();
            using var stream = File.OpenRead(source);
            var list = await JsonSerializer.DeserializeAsync<List<Student>>(stream , cancellationToken: cancellationToken);
            return list ?? new List<Student>();
        }
    }
}
