using System.Globalization;
using SeatFlow.Core.Exporters;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using CsvHelper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Exporters
{
    public class CsvSeatingExporter (ILogger<CsvSeatingExporter>? logger = null) : ISeatingPlanExporter
    {
        private readonly ILogger<CsvSeatingExporter> _logger = logger ?? NullLogger<CsvSeatingExporter>.Instance;

        public ExportFormat Format => ExportFormat.Csv;
        public async Task ExportAsync (SeatingPlan plan , string path , CancellationToken cancellationToken = default)
        {
            await ExportAsync(plan , path , new ExportOptions { Format = ExportFormat.Csv } , cancellationToken);
        }

        public async Task ExportAsync (SeatingPlan plan , string path , ExportOptions options , CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("CSV 座位导出开始：{Path}（{Count} 条记录）" , path , plan.Assignments.Count);
            var bytes = await BuildCsvBytesAsync(plan , options , cancellationToken);
            await System.IO.File.WriteAllBytesAsync(path , bytes , cancellationToken);
            _logger.LogInformation("CSV 座位导出完成: {Path}，{Count} 条记录" , path , plan.Assignments.Count);
        }

        /// <inheritdoc />
        public async Task<byte[]> ExportBytesAsync (SeatingPlan plan , ExportOptions options , CancellationToken cancellationToken = default)
        {
            return await BuildCsvBytesAsync(plan , options , cancellationToken);
        }

        private static async Task<byte[]> BuildCsvBytesAsync (SeatingPlan plan , ExportOptions options , CancellationToken ct)
        {
            var records = plan.Assignments.Select(kv => new SeatAssignmentRecord
            {
                SeatId = kv.Key ,
                StudentId = options.Anonymize ? "***" : kv.Value
            }).ToList();

            using var ms = new MemoryStream();
            await using (var writer = new StreamWriter(ms , new System.Text.UTF8Encoding(true) , leaveOpen: true))
            await using (var csv = new CsvWriter(writer , CultureInfo.InvariantCulture))
            {
                await csv.WriteRecordsAsync(records , ct);
            }
            return ms.ToArray();
        }

        public async Task ExportLayoutAsync (LayoutSeatingExportModel model , string path , ExportOptions options , CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("CSV 布局导出开始: {Path}" , path);
            var bytes = await BuildLayoutCsvBytesAsync(model , cancellationToken);
            await System.IO.File.WriteAllBytesAsync(path , bytes , cancellationToken);
            _logger.LogInformation("CSV 布局导出完成: {Path}" , path);
        }

        /// <inheritdoc />
        public async Task<byte[]> ExportLayoutBytesAsync (LayoutSeatingExportModel model , ExportOptions options , CancellationToken cancellationToken = default)
        {
            return await BuildLayoutCsvBytesAsync(model , cancellationToken);
        }

        private static async Task<byte[]> BuildLayoutCsvBytesAsync (LayoutSeatingExportModel model , CancellationToken ct)
        {
            using var ms = new MemoryStream();
            await using (var writer = new StreamWriter(ms , new System.Text.UTF8Encoding(true) , leaveOpen: true))
            {
                await writer.WriteLineAsync($"# {model.LayoutName}");
                foreach (var row in model.Rows)
                    await writer.WriteLineAsync(string.Join("," , row.Cells.Select(c => EscapeCsv(c.Text))));
                await writer.FlushAsync(ct);
            }
            return ms.ToArray();
        }

        private static string EscapeCsv (string text)
        {
            if (text.Contains(',') || text.Contains('"') || text.Contains('\n'))
                return $"\"{text.Replace("\"" , "\"\"")}\"";
            return text;
        }

        private class SeatAssignmentRecord
        {
            public string SeatId { get; set; } = string.Empty;
            public string StudentId { get; set; } = string.Empty;
        }
    }
}
