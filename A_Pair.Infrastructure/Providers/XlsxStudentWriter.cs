using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using OfficeOpenXml;

namespace A_Pair.Infrastructure.Providers
{
    public class XlsxStudentWriter : IStudentWriter
    {
        public async Task WriteAsync (string path , IEnumerable<Student> students , CancellationToken cancellationToken = default)
        {
            ExcelPackage.License.SetNonCommercialPersonal("A_Pair");
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Students");

            // 表头
            ws.Cells[1 , 1].Value = "学号";
            ws.Cells[1 , 2].Value = "姓名";
            ws.Cells[1 , 3].Value = "性别";
            ws.Cells[1 , 4].Value = "身高";
            ws.Cells[1 , 5].Value = "需前排";

            int row = 2;
            foreach (var s in students)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ws.Cells[row , 1].Value = s.Id;
                ws.Cells[row , 2].Value = s.Name;
                ws.Cells[row , 3].Value = s.Gender?.ToString() ?? "";
                ws.Cells[row , 4].Value = s.Height;
                ws.Cells[row , 5].Value = s.NeedsFrontRow;
                row++;
            }

            await package.SaveAsAsync(new FileInfo(path) , cancellationToken);
        }
    }
}