using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Workspace;

namespace A_Pair.Core.Exporters
{
    public interface ISeatingPlanExporter
    {
        // 原有方法，保持兼容
        Task ExportAsync (SeatingPlan plan , string path , CancellationToken cancellationToken = default);

        // 新方法，支持导出选项
        Task ExportAsync (SeatingPlan plan , string path , A_Pair.Core.Models.ExportOptions options , CancellationToken cancellationToken = default);
    }
}