using System.IO;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Exporters;
using A_Pair.Core.Models;
using A_Pair.Core.Workspace;
using OfficeOpenXml;

namespace A_Pair.Infrastructure.Exporters
{
    public class ExcelSeatingExporter : ISeatingPlanExporter
    {
        public async Task ExportAsync (SeatingPlan plan , string path , CancellationToken cancellationToken = default)
        {
            await ExportAsync(plan , path , new ExportOptions { Format = ExportFormat.Excel } , cancellationToken);
        }

        public async Task ExportAsync (SeatingPlan plan , string path , ExportOptions options , CancellationToken cancellationToken = default)
        {
            try { ExcelPackage.License.SetNonCommercialPersonal("FullName"); } catch { }

            try
            {
                using var p = new ExcelPackage();
                var ws = p.Workbook.Worksheets.Add("Seating");
                ws.Cells[1 , 1].Value = "SeatId";
                ws.Cells[1 , 2].Value = options.Anonymize ? "StudentId (anonymized)" : "StudentId";
                int r = 2;
                foreach (var kv in plan.Assignments)
                {
                    ws.Cells[r , 1].Value = kv.Key;
                    ws.Cells[r , 2].Value = options.Anonymize ? "***" : kv.Value;
                    r++;
                }

                if (options.IncludeMetadata)
                {
                    var metaWs = p.Workbook.Worksheets.Add("Metadata");
                    metaWs.Cells[1 , 1].Value = "Property";
                    metaWs.Cells[1 , 2].Value = "Value";
                    metaWs.Cells[2 , 1].Value = "ExportTime";
                    metaWs.Cells[2 , 2].Value = System.DateTime.Now.ToString("O");
                    metaWs.Cells[3 , 1].Value = "SeatCount";
                    metaWs.Cells[3 , 2].Value = plan.Assignments.Count;
                }

                var fi = new FileInfo(path);
                await p.SaveAsAsync(fi , cancellationToken);
            }
            catch
            {
                var lines = new System.Collections.Generic.List<string> { "SeatId,StudentId" };
                foreach (var kv in plan.Assignments)
                    lines.Add($"{kv.Key},{(options.Anonymize ? "***" : kv.Value)}");
                await System.IO.File.WriteAllLinesAsync(path , lines , cancellationToken);
            }
        }
    }
}