using A_Pair.Core.Models;
using A_Pair.Core.Providers;

namespace A_Pair.Infrastructure.Providers
{
    /// <summary>
    /// CSV 格式的学生数据提供器，手动解析以支持中英文列名和注释行。
    /// </summary>
    /// <remarks>
    /// 第 1 行为列名（支持中英文），第 2 行为注释行（自动跳过），数据从第 3 行开始。
    /// 支持的列名：姓名/Name、身高/Height、性别/Gender、需要前排/NeedsFrontRow。
    /// </remarks>
    public class CsvStudentProvider : IStudentProvider
    {
        public async Task<List<Student>> LoadAsync (string source , CancellationToken cancellationToken = default)
        {
            var list = new List<Student>();
            if (string.IsNullOrEmpty(source) || !File.Exists(source)) return list;

            var lines = await File.ReadAllLinesAsync(source , cancellationToken);
            if (lines.Length < StudentDataMapping.DataStartRow) return list;

            // 第 1 行：列名
            var headers = ParseCsvLine(lines[0]);
            var columnMap = new Dictionary<int , string>();
            for (int i = 0; i < headers.Length; i++)
            {
                var prop = StudentDataMapping.ResolveProperty(headers[i]);
                if (prop != null)
                    columnMap[i] = prop;
            }

            // 从第 3 行开始读取（第 2 行为注释行）
            for (int r = StudentDataMapping.DataStartRow - 1; r < lines.Length; r++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = ParseCsvLine(lines[r]);
                var student = new Student();
                foreach (var (idx , prop) in columnMap)
                {
                    var raw = idx < values.Length ? values[idx] : null;
                    StudentDataMapping.SetProperty(student , prop , raw);
                }
                if (!string.IsNullOrWhiteSpace(student.Name))
                    list.Add(student);
            }

            return list;
        }

        private static string[] ParseCsvLine (string line)
        {
            var result = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else if (ch == '"')
                    {
                        inQuotes = false;
                    }
                    else
                    {
                        current.Append(ch);
                    }
                }
                else
                {
                    if (ch == '"')
                    {
                        inQuotes = true;
                    }
                    else if (ch == ',')
                    {
                        result.Add(current.ToString().Trim());
                        current.Clear();
                    }
                    else
                    {
                        current.Append(ch);
                    }
                }
            }
            result.Add(current.ToString().Trim());
            return result.ToArray();
        }
    }
}
