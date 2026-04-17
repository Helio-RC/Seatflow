using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Exporters;
using A_Pair.Core.Workspace;
using CsvHelper;

namespace A_Pair.Infrastructure.Exporters
{
    public class CsvSeatingExporter : ISeatingPlanExporter
    {
        public async Task ExportAsync(SeatingPlan plan, string path, CancellationToken cancellationToken = default)
        {
            var records = plan.Assignments.Select(kv => new SeatAssignmentRecord
            {
                SeatId = kv.Key,
                StudentId = kv.Value
            }).ToList();

            await using var writer = new StreamWriter(path);
            await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            await csv.WriteRecordsAsync(records, cancellationToken);
        }

        private class SeatAssignmentRecord
        {
            public string SeatId { get; set; } = string.Empty;
            public string StudentId { get; set; } = string.Empty;
        }
    }
}