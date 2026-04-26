using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Workspace;
using A_Pair.Core.Models;

namespace A_Pair.Core.Exporters
{
    /// <summary>
    /// 座位安排计划导出器接口，定义将 <see cref="SeatingPlan"/> 导出到文件的标准契约。
    /// 支持多种导出格式（Excel、CSV、PDF、JSON），由基础设施层实现。
    /// </summary>
    public interface ISeatingPlanExporter
    {
        ExportFormat Format { get; }
        /// <summary>
        /// 使用默认选项导出座位安排计划（向后兼容）。
        /// </summary>
        /// <param name="plan">座位安排计划。</param>
        /// <param name="path">导出文件路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        Task ExportAsync (SeatingPlan plan , string path , CancellationToken cancellationToken = default);

        /// <summary>
        /// 使用指定的导出选项导出座位安排计划。
        /// 支持匿名化、包含元数据等高级选项。
        /// </summary>
        /// <param name="plan">座位安排计划。</param>
        /// <param name="path">导出文件路径。</param>
        /// <param name="options">导出选项。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        Task ExportAsync (SeatingPlan plan , string path , ExportOptions options , CancellationToken cancellationToken = default);
    }
}