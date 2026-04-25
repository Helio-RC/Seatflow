using A_Pair.Core.Workspace;

namespace A_Pair.Core.Strategies
{
    /// <summary>
    /// 随机填充策略，优先级最低（Priority=10）。
    /// 将尚未分配的学生随机填入空座位，作为兜底策略确保所有学生都有座位。
    /// </summary>
    public class RandomFillStrategy (Random random) : ISeatingStrategy
    {
        private readonly Random _random = random ?? throw new ArgumentNullException(nameof(random));

        /// <summary>
        /// 使用默认随机数种子创建实例。
        /// </summary>
        public RandomFillStrategy () : this(new Random()) { }

        /// <summary>策略 ID："RandomFill"。</summary>
        public string Id { get; } = "RandomFill";

        /// <summary>策略名称："RandomFill"。</summary>
        public string Name { get; } = "RandomFill";

        /// <summary>执行优先级：10（最低优先级，最后执行）。</summary>
        public int Priority { get; set; } = 10;

        /// <summary>是否启用。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 执行随机填充：将未分配的学生打乱后依次填入空座位。
        /// </summary>
        public Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            var emptySeats = workspace.GetEmptySeats().ToList();
            var students = workspace.Students.Where(s => !workspace.BuildSeatingPlan().Assignments.ContainsValue(s.Id)).ToList();

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

            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        /// <summary>随机填充策略无需配置，始终有效。</summary>
        public ValidationResult ValidateConfiguration () => new() { IsValid = true };
    }
}
