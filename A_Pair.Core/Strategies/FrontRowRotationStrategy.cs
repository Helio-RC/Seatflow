using A_Pair.Core.Models;
using A_Pair.Core.Workspace;

namespace A_Pair.Core.Strategies
{
    /// <summary>
    /// 前排轮换策略（Priority=30），基于累计分数公平分配前排座位。
    /// 分数计算公式：总分 = NeedsFrontRow加分 + FrontRowPreferenceScore - (历史前排次数 × HistoryWeight)。
    /// 分数越高的学生越优先分配到前排，确保轮换公平性。
    /// </summary>
    public class FrontRowRotationStrategy (FrontRowRotationStrategy.FrontRowRotationConfiguration config) : ISeatingStrategy
    {
        private readonly FrontRowRotationConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));

        /// <summary>
        /// 使用默认配置创建实例。
        /// </summary>
        public FrontRowRotationStrategy () : this(new FrontRowRotationConfiguration()) { }

        /// <summary>获取策略配置对象，供 Application 层读取和修改配置参数。</summary>
        public FrontRowRotationConfiguration Config => _config;

        /// <summary>设置前排行数（从布局元数据同步）。</summary>
        public void SetFrontRowCount (int count) => _config.FrontRowCount = Math.Max(1 , count);

        /// <summary>策略 ID："FrontRowRotation"。</summary>
        public string Id { get; } = "FrontRowRotation";

        /// <summary>策略名称："FrontRowRotation"。</summary>
        public string Name { get; } = "FrontRowRotation";

        /// <summary>执行优先级：30。</summary>
        public int Priority { get; set; } = 30;

        /// <summary>是否启用。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 执行前排轮换：
        /// 1. 识别网格布局最前行 / 极坐标布局最外层环。
        /// 2. 计算每个未分配学生的前排需求分数。
        /// 3. 按分数从高到低分配前排座位。
        /// </summary>
        public Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            var emptySeats = workspace.GetEmptySeats().ToList();
            if (emptySeats.Count == 0)
                return Task.FromResult(new StrategyExecutionResult { Success = true });

            // 收集前排座位（Grid + Polar）
            var frontRowSeats = new List<Seat>();

            var gridSeats = emptySeats.OfType<GridSeat>().ToList();
            if (gridSeats.Count > 0)
            {
                int frontRowMin = gridSeats.Min(s => s.Row);
                int frontRowMax = frontRowMin + _config.FrontRowCount - 1;
                frontRowSeats.AddRange(gridSeats.Where(s => s.Row >= frontRowMin && s.Row <= frontRowMax));
            }

            var polarSeats = emptySeats.OfType<PolarSeat>().ToList();
            if (polarSeats.Count > 0)
            {
                int maxRing = polarSeats.Max(s => s.Ring);
                int frontRingMin = maxRing - _config.FrontRowCount + 1;
                frontRowSeats.AddRange(polarSeats.Where(s => s.Ring >= frontRingMin));
            }

            var freeformSeats = emptySeats.OfType<FreeformSeat>().Where(s => s.Row.HasValue).ToList();
            if (freeformSeats.Count > 0)
            {
                int frontRowMin = freeformSeats.Min(s => s.Row!.Value);
                int frontRowMax = frontRowMin + _config.FrontRowCount - 1;
                frontRowSeats.AddRange(freeformSeats.Where(s => s.Row >= frontRowMin && s.Row <= frontRowMax));
            }

            if (frontRowSeats.Count == 0)
                return Task.FromResult(new StrategyExecutionResult { Success = true });

            // 获取尚未分配的学生
            var assignedStudentIds = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();
            var availableStudents = workspace.Students.Where(s => !assignedStudentIds.Contains(s.Id)).ToList();

            // 计算每个学生对前排的"需求度分数"
            var frontSeatIds = new HashSet<string>(frontRowSeats.Select(s => s.Id));

            var studentScores = availableStudents.Select(s =>
            {
                int frontRowHistoryCount = s.RecentSeatHistory.GetAll()
                    .Count(seatId => frontSeatIds.Contains(seatId));

                int score = (s.NeedsFrontRow ? _config.NeedsFrontRowBonus : 0)
            + s.FrontRowPreferenceScore
            - (frontRowHistoryCount * _config.HistoryWeight);
                return new { Student = s , Score = score };
            }).OrderByDescending(x => x.Score).ToList();

            int assignCount = Math.Min(frontRowSeats.Count , studentScores.Count);
            for (int i = 0; i < assignCount && !cancellationToken.IsCancellationRequested; i++)
            {
                workspace.TryAssignSeat(frontRowSeats[i].Id , studentScores[i].Student.Id , out _);
            }

            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        /// <summary>
        /// 验证配置：HistoryWeight 不能为负数。
        /// </summary>
        public ValidationResult ValidateConfiguration ()
        {
            if (_config.HistoryWeight < 0)
            {
                return new ValidationResult { IsValid = false , Error = "HistoryWeight must be non-negative." };
            }
            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// 前排轮换策略的配置参数。
        /// </summary>
        public class FrontRowRotationConfiguration
        {
            /// <summary>
            /// 历史座位权重系数，每次坐过前排扣除的分数。
            /// 值越大，轮换公平性越强（避免同一学生连续坐前排）。
            /// </summary>
            public int HistoryWeight { get; set; } = 10;

            /// <summary>
            /// 特殊需求（如视力不佳）的固定加分。
            /// 设置为较大值（默认 1000）可确保有需求的学生优先获得前排座位。
            /// </summary>
            public int NeedsFrontRowBonus { get; set; } = 1000;

            /// <summary>
            /// 前排行数。默认为 1（仅第 1 行），可根据教室布局配置。
            /// </summary>
            public int FrontRowCount { get; set; } = 1;
        }
    }
}