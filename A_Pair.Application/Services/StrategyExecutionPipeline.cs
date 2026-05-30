using A_Pair.Application.Interfaces;
using A_Pair.Core.Strategies;
using A_Pair.Core.Workspace;
using Microsoft.Extensions.Logging;

namespace A_Pair.Application.Services
{
    /// <summary>
    /// 策略执行管道，按优先级升序依次执行多个座位分配策略。
    /// </summary>
    /// <remarks>
    /// <b>执行模型：基线 → 优化 → 最终裁决</b>
    /// <para>
    /// 策略按 <see cref="ISeatingStrategy.Priority"/> 升序执行：
    /// </para>
    /// <list type="number">
    /// <item><b>基线阶段</b>（低 Priority）：建立初始全量分配（如 RandomFill=10）</item>
    /// <item><b>优化阶段</b>（中 Priority）：对特定维度进行调整（如 FrontRowRotation=30、DeskMate=50）</item>
    /// <item><b>裁决阶段</b>（高 Priority）：强制执行不可妥协的约束（如 FixedSeat=100）</item>
    /// </list>
    /// <para>
    /// 所有策略操作同一个 <see cref="SeatingWorkspace"/> 实例，后执行的策略可以通过
    /// 清空 OccupantId + TryAssignSeat 覆盖前序策略的结果。这意味着 <b>高 Priority 策略
    /// 具有最终决定权</b>——这是有意设计，不是 bug。
    /// </para>
    /// <para>
    /// 执行过程中通过 <see cref="IProgress{T}"/> 报告进度。
    /// </para>
    /// </remarks>
    public class StrategyExecutionPipeline
    {
        private readonly List<ISeatingStrategy> _strategies = [];
        private readonly ILogger<StrategyExecutionPipeline>? _logger;
        private readonly bool _throwOnFailure;

        /// <summary>
        /// 初始化策略执行管道，并按优先级排序策略列表。
        /// </summary>
        /// <param name="strategies">要执行的策略集合。</param>
        public StrategyExecutionPipeline (
            IEnumerable<ISeatingStrategy> strategies ,
            ILogger<StrategyExecutionPipeline>? logger = null ,
            bool throwOnFailure = false)
        {
            _strategies.AddRange(strategies.OrderBy(s => s.Priority));
            _logger = logger;
            _throwOnFailure = throwOnFailure;
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
                    failedStrategies.Add($"{strategy.Name}({strategy.Id}): {result.Message}");
                    _logger?.LogWarning("策略执行失败: {StrategyName} ({StrategyId}) - {Message}" ,
                        strategy.Name , strategy.Id , result.Message);
                }
            }

            if (failedStrategies.Count != 0 && _throwOnFailure)
            {
                throw new StrategyExecutionException($"以下策略执行失败: {string.Join("; " , failedStrategies)}");
            }

            return workspace.BuildSeatingPlan();
        }
    }

    public class StrategyExecutionException (string message) : System.Exception(message)
    {
    }
}
