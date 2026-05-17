using System.Globalization;
using A_Pair.Core.Exporters;
using A_Pair.Core.Models;
using A_Pair.Core.Workspace;
using CsvHelper;

namespace A_Pair.Infrastructure.Exporters
{
    public class CsvSeatingExporter : ISeatingPlanExporter
    {
        public ExportFormat Format => ExportFormat.Csv;
        public async Task ExportAsync (SeatingPlan plan , string path , CancellationToken cancellationToken = default)
        {
            await ExportAsync(plan , path , new ExportOptions { Format = ExportFormat.Csv } , cancellationToken);
        }

        public async Task ExportAsync (SeatingPlan plan , string path , ExportOptions options , CancellationToken cancellationToken = default)
        {
            var records = plan.Assignments.Select(kv => new SeatAssignmentRecord
            {
                SeatId = kv.Key ,
                StudentId = options.Anonymize ? "***" : kv.Value
            }).ToList();

            await using var writer = new StreamWriter(path , false , new System.Text.UTF8Encoding(true));
            await using var csv = new CsvWriter(writer , CultureInfo.InvariantCulture);
            await csv.WriteRecordsAsync(records , cancellationToken);
        }

        public Task ExportLayoutAsync (LayoutSeatingExportModel model , string path , ExportOptions options , CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var writer = new StreamWriter(path , false , new System.Text.UTF8Encoding(true));
            writer.WriteLine($"# {model.LayoutName}");
            foreach (var row in model.Rows)
                writer.WriteLine(string.Join("," , row.Cells.Select(c => EscapeCsv(c.Text))));
            return Task.CompletedTask;
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
