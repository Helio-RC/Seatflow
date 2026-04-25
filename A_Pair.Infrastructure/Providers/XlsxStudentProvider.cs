using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using OfficeOpenXml;

namespace A_Pair.Infrastructure.Providers
{
    /// <summary>
    /// XLSX 格式的学生数据提供器，使用 EPPlus 库从 Excel 文件读取学生列表。
    /// </summary>
    /// <remarks>
    /// 读取 Excel 文件的第一个工作表，从第二行开始逐行读取。
    /// 第一列（A）为学生姓名，第二列（B）为学生 ID（可选，为空时自动生成 GUID）。
    /// </remarks>
    public class XlsxStudentProvider : IStudentProvider
    {
        /// <inheritdoc />
        public async Task<List<Student>> LoadAsync (string source , CancellationToken cancellationToken = default)
        {
            var list = new List<Student>();
            if (string.IsNullOrEmpty(source) || !File.Exists(source)) return list;

            ExcelPackage.License.SetNonCommercialPersonal("A_Pair");
            using var stream = File.OpenRead(source);
            using var package = new ExcelPackage(stream);
            var ws = package.Workbook.Worksheets[0];
            if (ws.Dimension == null) return list;

            for (int r = ws.Dimension.Start.Row + 1; r <= ws.Dimension.End.Row; r++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = ws.Cells[r , 1].GetValue<string>();
                var id = ws.Cells[r , 2].GetValue<string>();
                list.Add(new Student { Name = name ?? string.Empty , Id = id ?? System.Guid.NewGuid().ToString() });
            }

            await Task.CompletedTask;
            return list;
        }
    }
}
