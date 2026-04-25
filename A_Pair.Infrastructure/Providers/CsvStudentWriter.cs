using System.Globalization;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using CsvHelper;

namespace A_Pair.Infrastructure.Providers
{
    /// <summary>
    /// CSV 格式的学生数据写入器，使用 CsvHelper 库将学生列表写入 CSV 文件。
    /// </summary>
    /// <remarks>
    /// 自动根据 <see cref="Student"/> 类的属性名生成 CSV 列头，并将所有学生记录写入文件。
    /// </remarks>
    public class CsvStudentWriter : IStudentWriter
    {
        /// <inheritdoc />
        public async Task WriteAsync (string path , IEnumerable<Student> students , CancellationToken cancellationToken = default)
        {
            await using var writer = new StreamWriter(path);
            await using var csv = new CsvWriter(writer , CultureInfo.InvariantCulture);
            await csv.WriteRecordsAsync(students , cancellationToken);
        }
    }
}