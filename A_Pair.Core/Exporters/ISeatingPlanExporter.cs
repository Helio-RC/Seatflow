using System.Threading;
using System.Threading.Tasks;

namespace A_Pair.Core.Exporters
{
    public interface ISeatingPlanExporter
    {
        Task ExportAsync(Workspace.SeatingPlan plan, string path, CancellationToken cancellationToken = default);
    }
}
