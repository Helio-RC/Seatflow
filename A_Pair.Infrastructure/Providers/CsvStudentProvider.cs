using System.Globalization;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using CsvHelper;

namespace A_Pair.Infrastructure.Providers
{
    /// <summary>
    /// CSV 格式的学生数据提供器，使用 CsvHelper 库从 CSV 文件读取学生列表。
    /// </summary>
    /// <remarks>
    /// CSV 文件的列名应与 <see cref="Student"/> 类的属性名匹配，CsvHelper 会自动映射。
    /// 如果文件路径为空或不存在，则返回空列表。
    /// </remarks>
    public class CsvStudentProvider : IStudentProvider
    {
        /// <inheritdoc />
        public async Task<List<Student>> LoadAsync (string source , CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(source)) return new List<Student>();

            using var reader = new StreamReader(source);
            using var csv = new CsvReader(reader , CultureInfo.InvariantCulture);

            var records = csv.GetRecordsAsync<Student>(cancellationToken);
            var list = new List<Student>();
            await foreach (var r in records.WithCancellation(cancellationToken))
            {
                list.Add(r);
            }

            return list;
        }
    }
}
