using A_Pair.Application.Interfaces;
using A_Pair.Core.Strategies;
using A_Pair.Core.Workspace;

namespace A_Pair.Application.Services
{
    /// <summary>
    /// 策略执行管道，按优先级顺序依次执行多个座位分配策略。
    /// </summary>
    /// <remarks>
    /// 策略按 <see cref="ISeatingStrategy.Priority"/> 升序执行（数值越小优先级越高）。
    /// 每个策略在同一个 <see cref="SeatingWorkspace"/> 上操作，后执行的策略可以覆盖或补充
    /// 先前策略的分配结果。执行过程中通过 <see cref="IProgress{T}"/> 报告进度。
    /// </remarks>
    public class StrategyExecutionPipeline
    {
        private readonly List<ISeatingStrategy> _strategies = [];

        /// <summary>
        /// 初始化策略执行管道，并按优先级排序策略列表。
        /// </summary>
        /// <param name="strategies">要执行的策略集合。</param>
        public StrategyExecutionPipeline (IEnumerable<ISeatingStrategy> strategies)
        {
            _strategies.AddRange(strategies.OrderBy(s => s.Priority));
        }

        /// <summary>
        /// 依次执行所有已启用的策略，并返回最终的座位安排计划。
        /// </summary>
        /// <param name="workspace">当前座位工作区，所有策略将在此工作区上操作。</param>
        /// <param name="progress">用于报告执行进度的对象。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>执行完成后生成的座位安排计划。</returns>
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
