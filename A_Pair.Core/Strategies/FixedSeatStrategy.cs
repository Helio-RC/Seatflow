using A_Pair.Core.Workspace;

namespace A_Pair.Core.Strategies
{
    /// <summary>
    /// 固定座位策略，优先级最高（Priority=100，最后执行）。
    /// 将指定学生强制分配到固定座位，不受其他策略影响。
    /// 适用于有特殊需求的学生（如残障学生固定前排座位）。
    /// </summary>
    public class FixedSeatStrategy (FixedSeatConfiguration config) : ISeatingStrategy
    {
        private readonly FixedSeatConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));

        /// <summary>
        /// 使用默认空配置创建实例。
        /// </summary>
        public FixedSeatStrategy () : this(new FixedSeatConfiguration()) { }

        /// <summary>策略 ID："FixedSeat"。</summary>
        public string Id { get; } = "FixedSeat";

        /// <summary>策略名称："FixedSeat"。</summary>
        public string Name { get; } = "FixedSeat";

        /// <summary>执行优先级：100（最高优先级，最后执行以确保覆盖）。</summary>
        public int Priority { get; set; } = 100;

        /// <summary>是否启用。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 执行固定座位分配：
        /// 1. 根据配置将指定座位标记为 IsFixed 并分配学生。
        /// 2. 确保所有固定座位保持不可用状态（不被其他策略修改）。
        /// </summary>
        public Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            if (workspace is null) throw new ArgumentNullException(nameof(workspace));

            foreach (var kv in _config.FixedAssignments)
            {
                var seat = workspace.FindSeats(s => s.Id == kv.Key).FirstOrDefault();
                if (seat == null) continue;

                // 先分配再固定，避免 IsFixed 导致分配失败
                if (!string.IsNullOrEmpty(kv.Value))
                {
                    // 如果座位已被其他人占用则清除（但不覆盖非固定？这里为了固定座位强制）
                    if (seat.OccupantId != null && seat.OccupantId != kv.Value)
                    {
                        seat.OccupantId = null;
                        seat.IsAvailable = true;
                    }
                    bool success = workspace.TryAssignSeat(seat.Id , kv.Value , out _);
                    if (success)
                    {
                        seat.IsFixed = true;
                    }
                }
                else
                {
                    // 如果配置中只有座位ID没有学生ID，仅标记为固定但不清除当前占用
                    seat.IsFixed = true;
                }
            }

            // 确保所有标记为 IsFixed 的座位保持不变
            foreach (var seat in workspace.FindSeats(s => s.IsFixed))
            {
                if (!string.IsNullOrEmpty(seat.OccupantId))
                {
                    seat.IsAvailable = false;
                }
            }

            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        /// <summary>
        /// 验证配置：FixedAssignments 不能为 null。
        /// </summary>
        public ValidationResult ValidateConfiguration ()
        {
            if (_config.FixedAssignments == null)
            {
                return new ValidationResult { IsValid = false , Error = "FixedAssignments cannot be null." };
            }
            return new ValidationResult { IsValid = true };
        }
    }

    /// <summary>
    /// 固定座位策略的配置，定义座位到学生的固定映射关系。
    /// </summary>
    public class FixedSeatConfiguration
    {
        /// <summary>固定分配字典，Key 为座位 ID，Value 为学生 ID。</summary>
        public Dictionary<string , string> FixedAssignments { get; set; } = [];
    }
}