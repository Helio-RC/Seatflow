using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using OfficeOpenXml;

namespace A_Pair.Infrastructure.Providers
{
    public class XlsxStudentProvider : IStudentProvider
    {
        public async Task<List<Student>> LoadAsync(string source, CancellationToken cancellationToken = default)
        {
            var list = new List<Student>();
            if (string.IsNullOrEmpty(source) || !File.Exists(source)) return list;

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var stream = File.OpenRead(source);
            using var package = new ExcelPackage(stream);
            var ws = package.Workbook.Worksheets[0];
            if (ws.Dimension == null) return list;

            for (int r = ws.Dimension.Start.Row + 1; r <= ws.Dimension.End.Row; r++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = ws.Cells[r, 1].GetValue<string>();
                var id = ws.Cells[r, 2].GetValue<string>();
                list.Add(new Student { Name = name ?? string.Empty, Id = id ?? System.Guid.NewGuid().ToString() });
            }

            await Task.CompletedTask;
            return list;
        }
    }
}
