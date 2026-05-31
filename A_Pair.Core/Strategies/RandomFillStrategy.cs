using A_Pair.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Core.Strategies
{
    /// <summary>
    /// 随机填充策略（Priority=100，最后执行，填满所有剩余座位）。
    /// 作为管道中的兜底策略，在所有其他策略完成分配后运行，
    /// 将剩余未分配学生随机填入剩余空座。确保发布会场没有空座或遗漏学生。
    /// </summary>
    public class RandomFillStrategy : ISeatingStrategy
    {
        private readonly Random _random;
        private readonly ILogger<RandomFillStrategy> _logger;

        public RandomFillStrategy (Random random , ILogger<RandomFillStrategy>? logger = null)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _logger = logger ?? NullLogger<RandomFillStrategy>.Instance;
        }

        /// <summary>
        /// 使用默认随机数种子创建实例。
        /// </summary>
        public RandomFillStrategy () : this(new Random()) { }

        /// <summary>策略 ID："RandomFill"。</summary>
        public string Id { get; } = "RandomFill";

        /// <summary>策略名称："RandomFill"。</summary>
        public string Name { get; } = "RandomFill";

        /// <summary>执行优先级：100（最后执行，填满所有剩余非固定空座）。</summary>
        public int Priority { get; set; } = 100;

        /// <summary>是否启用。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 执行随机填充：将未分配的学生打乱后依次填入空座位。
        /// </summary>
        public Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            cancellationToken.ThrowIfCancellationRequested();

            var emptySeats = workspace.GetEmptySeats().ToList();
            var assignedIds = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();
            var students = workspace.Students.Where(s => !assignedIds.Contains(s.Id)).ToList();

            _logger.LogInformation("RandomFill 策略开始执行：{EmptySeats} 个空座位，{Unassigned} 名未分配学生" ,
                emptySeats.Count , students.Count);

            // 使用 Fisher-Yates 洗牌算法打乱学生顺序
            for (int i = students.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (students[j] , students[i]) = (students[i] , students[j]);
            }

            int assignCount = Math.Min(emptySeats.Count , students.Count);
            for (int i = 0; i < assignCount && !cancellationToken.IsCancellationRequested; i++)
            {
                var seat = emptySeats[i];
                var student = students[i];
                workspace.TryAssignSeat(seat.Id , student.Id , out _);
            }

            _logger.LogInformation("RandomFill 策略完成：分配 {Assigned} 名学生，{Unfilled} 个座位空置" ,
                assignCount , Math.Max(0 , emptySeats.Count - assignCount));
            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        /// <summary>随机填充策略无需配置，始终有效。</summary>
        public ValidationResult ValidateConfiguration () => new() { IsValid = true };
    }
}
