using System.Globalization;
using A_Pair.Core.Exporters;
using A_Pair.Core.Models;
using A_Pair.Core.Workspace;
using CsvHelper;

namespace A_Pair.Infrastructure.Exporters
{
    /// <summary>
    /// CSV 格式的座位安排导出器，使用 CsvHelper 库生成逗号分隔值文件。
    /// </summary>
    /// <remarks>
    /// 输出两列数据：座位 ID（SeatId）和学生 ID（StudentId）。
    /// 当 <see cref="ExportOptions.Anonymize"/> 为 true 时，学生 ID 将被替换为 "***"。
    /// </remarks>
    public class CsvSeatingExporter : ISeatingPlanExporter
    {
        public ExportFormat Format => ExportFormat.Csv;
        /// <inheritdoc />
        public async Task ExportAsync (SeatingPlan plan , string path , CancellationToken cancellationToken = default)
        {
            await ExportAsync(plan , path , new ExportOptions { Format = ExportFormat.Csv } , cancellationToken);
        }

        /// <inheritdoc />
        public async Task ExportAsync (SeatingPlan plan , string path , ExportOptions options , CancellationToken cancellationToken = default)
        {
            var records = plan.Assignments.Select(kv => new SeatAssignmentRecord
            {
                SeatId = kv.Key ,
                StudentId = options.Anonymize ? "***" : kv.Value
            }).ToList();

            await using var writer = new StreamWriter(path);
            await using var csv = new CsvWriter(writer , CultureInfo.InvariantCulture);
            await csv.WriteRecordsAsync(records , cancellationToken);
        }

        /// <summary>
        /// 座位分配记录的数据传输对象，用于 CsvHelper 的自动映射。
        /// </summary>
        private class SeatAssignmentRecord
        {
            /// <summary>座位 ID。</summary>
            public string SeatId { get; set; } = string.Empty;
            /// <summary>学生 ID（匿名模式下为 "***"）。</summary>
            public string StudentId { get; set; } = string.Empty;
        }
    }
}