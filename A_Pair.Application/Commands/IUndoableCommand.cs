using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Workspace;

namespace A_Pair.Application.Commands
{
    public interface IUndoableCommand
    {
        string Id { get; }
        Task<bool> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken = default);
        Task<bool> UndoAsync(SeatingWorkspace workspace, CancellationToken cancellationToken = default);
    }
}
