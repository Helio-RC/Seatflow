using System.Globalization;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using CsvHelper;
using CsvHelper.Configuration;

namespace SeatFlow.Infrastructure.Providers;

/// <summary>
/// CSV 格式的学生数据提供器，使用 CsvHelper 解析以正确处理引号字段和嵌入换行。
/// </summary>
/// <remarks>
/// 第 1 行为列名（支持中英文），第 2 行为注释行（自动跳过），数据从第 3 行开始。
/// 支持的列名：姓名/Name、身高/Height、性别/Gender、需要前排/NeedsFrontRow。
/// </remarks>
public class CsvStudentProvider : IStudentProvider
{
    private static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture)
    {
        ShouldSkipRecord = args => args.Row?.Context?.Parser?.Row == 2 // 跳过第 2 行（注释行）
    };

    public Task<List<Student>> LoadAsync (string source , CancellationToken cancellationToken = default)
    {
        var list = new List<Student>();
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
            return Task.FromResult(list);

        using var reader = new StreamReader(source);
        using var csv = new CsvReader(reader , Config);

        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord;
        if (headers is null || headers.Length == 0)
            return Task.FromResult(list);

        var columnMap = new Dictionary<int , string>();
        for (int i = 0; i < headers.Length; i++)
        {
            var prop = StudentDataMapping.ResolveProperty(headers[i]);
            if (prop != null)
                columnMap[i] = prop;
        }

        while (csv.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var student = new Student();
            foreach (var (idx , prop) in columnMap)
            {
                var raw = csv.TryGetField(idx , out string? value) ? value : null;
                StudentDataMapping.SetProperty(student , prop , raw);
            }
            if (!string.IsNullOrWhiteSpace(student.Name))
                list.Add(student);
        }

        return Task.FromResult(list);
    }
}
