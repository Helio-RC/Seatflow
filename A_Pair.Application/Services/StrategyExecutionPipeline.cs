using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Strategies;
using A_Pair.Core.Workspace;

namespace A_Pair.Application.Services
{
    public class StrategyExecutionPipeline
    {
        private readonly List<ISeatingStrategy> _strategies = new();

        public StrategyExecutionPipeline(IEnumerable<ISeatingStrategy> strategies)
        {
            _strategies.AddRange(strategies.OrderBy(s => s.Priority));
        }

        public async Task<SeatingPlan> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken)
        {
            foreach (var strategy in _strategies.Where(s => s.IsEnabled).OrderBy(s => s.Priority))
            {
                await strategy.ExecuteAsync(workspace, cancellationToken);
            }

            return workspace.BuildSeatingPlan();
        }
    }
}
