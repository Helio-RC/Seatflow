using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using OfficeOpenXml;

namespace A_Pair.Infrastructure.Providers
{
    /// <summary>
    /// XLSX 格式的学生数据提供器，使用 EPPlus 库从 Excel 文件读取学生列表。
    /// </summary>
    /// <remarks>
    /// 第 1 行为列名（支持中英文），第 2 行为注释行（自动跳过），数据从第 3 行开始。
    /// 支持的列名：姓名/Name、身高/Height、性别/Gender、需要前排/NeedsFrontRow。
    /// </remarks>
    public class XlsxStudentProvider : IStudentProvider
    {
        public async Task<List<Student>> LoadAsync(string source, CancellationToken cancellationToken = default)
        {
            var list = new List<Student>();
            if (string.IsNullOrEmpty(source) || !File.Exists(source)) return list;

            ExcelPackage.License.SetNonCommercialPersonal("A_Pair");
            using var stream = File.OpenRead(source);
            using var package = new ExcelPackage(stream);
            var ws = package.Workbook.Worksheets[0];
            if (ws.Dimension == null) return list;

            // 第 1 行：读取列名，建立 列索引 → 属性名 的映射
            var columnMap = new Dictionary<int, string>();
            for (int c = ws.Dimension.Start.Column; c <= ws.Dimension.End.Column; c++)
            {
                var header = ws.Cells[ws.Dimension.Start.Row, c].GetValue<string>();
                if (!string.IsNullOrWhiteSpace(header))
                {
                    var prop = StudentDataMapping.ResolveProperty(header);
                    if (prop != null)
                        columnMap[c] = prop;
                }
            }

            // 从第 3 行开始读取数据（第 2 行为注释行）
            for (int r = StudentDataMapping.DataStartRow; r <= ws.Dimension.End.Row; r++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var student = new Student();
                foreach (var (col, prop) in columnMap)
                {
                    var raw = ws.Cells[r, col].GetValue<string>();
                    StudentDataMapping.SetProperty(student, prop, raw);
                }
                if (!string.IsNullOrWhiteSpace(student.Name))
                    list.Add(student);
            }

            await Task.CompletedTask;
            return list;
        }
    }
}
