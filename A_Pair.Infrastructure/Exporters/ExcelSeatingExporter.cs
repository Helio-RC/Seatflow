using A_Pair.Core.Exporters;
using A_Pair.Core.Models;
using A_Pair.Core.Workspace;
using OfficeOpenXml;

namespace A_Pair.Infrastructure.Exporters
{
    /// <summary>
    /// Excel 格式的座位安排导出器，使用 EPPlus 库生成 .xlsx 文件。
    /// </summary>
    /// <remarks>
    /// 生成包含 "Seating" 工作表的 Excel 文件，列出座位 ID 和学生 ID 的对应关系。
    /// 当 <see cref="ExportOptions.IncludeMetadata"/> 为 true 时，额外生成 "Metadata" 工作表
    /// 包含导出时间和座位总数等元数据。
    /// 如果 EPPlus 写入失败（例如缺少许可证），会自动降级为 CSV 格式输出。
    /// </remarks>
    public class ExcelSeatingExporter : ISeatingPlanExporter
    {
        public ExportFormat Format => ExportFormat.Excel;
        /// <inheritdoc />
        public async Task ExportAsync (SeatingPlan plan , string path , CancellationToken cancellationToken = default)
        {
            await ExportAsync(plan , path , new ExportOptions { Format = ExportFormat.Excel } , cancellationToken);
        }

        /// <inheritdoc />
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
                // EPPlus 写入失败时降级为 CSV 格式
                var lines = new System.Collections.Generic.List<string> { "SeatId,StudentId" };
                foreach (var kv in plan.Assignments)
                    lines.Add($"{kv.Key},{(options.Anonymize ? "***" : kv.Value)}");
                await System.IO.File.WriteAllLinesAsync(path , lines , cancellationToken);
            }
        }
    }
}