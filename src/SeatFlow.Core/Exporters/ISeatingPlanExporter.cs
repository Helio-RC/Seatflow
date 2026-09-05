using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;

namespace SeatFlow.Core.Exporters
{
    /// <summary>
    /// 座位安排计划导出器接口，定义将 <see cref="SeatingPlan"/> 导出到文件的标准契约。
    /// 支持多种导出格式（Excel、CSV、PDF、JSON），由基础设施层实现。
    /// </summary>
    public interface ISeatingPlanExporter
    {
        /// <summary>导出格式类型。</summary>
        ExportFormat Format { get; }

        /// <summary>
        /// 使用默认选项导出座位安排计划（向后兼容）。
        /// </summary>
        Task ExportAsync (SeatingPlan plan , string path , CancellationToken cancellationToken = default);

        /// <summary>
        /// 使用指定的导出选项导出座位安排计划。
        /// </summary>
        Task ExportAsync (SeatingPlan plan , string path , ExportOptions options , CancellationToken cancellationToken = default);

        /// <summary>
        /// 使用结构化布局模型导出（保留行列/过道/环形结构，显示姓名而非 ID）。
        /// </summary>
        Task ExportLayoutAsync (LayoutSeatingExportModel model , string path , ExportOptions options , CancellationToken cancellationToken = default);

        /// <summary>
        /// 导出为字节流（WASM/浏览器端调用；桌面实现默认经临时文件回退，
        /// 内存友好的实现可覆盖）。
        /// </summary>
        async Task<byte[]> ExportBytesAsync (SeatingPlan plan , ExportOptions options , CancellationToken cancellationToken = default)
        {
            var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath() ,
                $"SeatFlow_{Guid.NewGuid():N}.export");
            try
            {
                await ExportAsync(plan , tmp , options , cancellationToken);
                return await System.IO.File.ReadAllBytesAsync(tmp , cancellationToken);
            }
            finally
            {
                if (System.IO.File.Exists(tmp))
                    System.IO.File.Delete(tmp);
            }
        }

        /// <summary>
        /// 使用结构化布局模型导出为字节流（WASM/浏览器端调用）。
        /// </summary>
        async Task<byte[]> ExportLayoutBytesAsync (LayoutSeatingExportModel model , ExportOptions options , CancellationToken cancellationToken = default)
        {
            var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath() ,
                $"SeatFlow_{Guid.NewGuid():N}.export");
            try
            {
                await ExportLayoutAsync(model , tmp , options , cancellationToken);
                return await System.IO.File.ReadAllBytesAsync(tmp , cancellationToken);
            }
            finally
            {
                if (System.IO.File.Exists(tmp))
                    System.IO.File.Delete(tmp);
            }
        }
    }
}
