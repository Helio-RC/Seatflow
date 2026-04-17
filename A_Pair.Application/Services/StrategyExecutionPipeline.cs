using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
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

        public async Task<SeatingPlan> ExecuteAsync (
    SeatingWorkspace workspace ,
    IProgress<SeatingProgress>? progress = null ,
    CancellationToken cancellationToken = default)
        {
            var enabledStrategies = _strategies.Where(s => s.IsEnabled).OrderBy(s => s.Priority).ToList();
            int total = enabledStrategies.Count;
            int current = 0;
            var failedStrategies = new List<string>();

            foreach (var strategy in enabledStrategies)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new SeatingProgress
                {
                    CurrentStep = ++current ,
                    TotalSteps = total ,
                    StatusMessage = $"正在执行策略: {strategy.Name}"
                });

                var result = await strategy.ExecuteAsync(workspace , cancellationToken);
                if (!result.Success)
                {
                    failedStrategies.Add($"{strategy.Name}: {result.Message}");
                }
            }

            // 如果有关键策略失败，可记录日志或抛出异常（此处仅忽略）
            return workspace.BuildSeatingPlan();
        }
    }
}
