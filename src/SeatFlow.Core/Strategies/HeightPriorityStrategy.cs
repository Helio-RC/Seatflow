using SeatFlow.Core.DomainServices;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Core.Strategies
{
    /// <summary>
    /// 身高优先策略（Priority=60，在固定座位之后、随机填充之前执行）。
    /// 将填写了身高的学生按从矮到高排序，优先填充前排座位
    /// （网格最小行 / 环形最内圈 / 自由点最小行）；未填写身高的学生不参与本策略，
    /// 由后续策略兜底。
    /// </summary>
    public class HeightPriorityStrategy(HeightPriorityStrategy.HeightPriorityConfiguration config, ILogger<HeightPriorityStrategy>? logger = null) : ISeatingStrategy
    {
        private readonly HeightPriorityConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
        private readonly ILogger<HeightPriorityStrategy> _logger = logger ?? NullLogger<HeightPriorityStrategy>.Instance;

        /// <summary>策略展示名称（与 manifest displayName 一致）。</summary>
        public const string DisplayNameConst = "身高优先";

        /// <summary>策略 ID："HeightPriority"。</summary>
        public string Id { get; } = "HeightPriority";

        /// <summary>策略名称："HeightPriority"。</summary>
        public string Name { get; } = "HeightPriority";

        /// <summary>执行优先级：60（固定座位之后、前排轮换与随机填充之前）。</summary>
        public int Priority { get; set; } = 60;

        /// <summary>是否启用（默认关闭，避免改变现有排座结果）。</summary>
        public bool IsEnabled { get; set; } = false;

        /// <summary>获取策略配置对象，供 Application 层读取和修改配置参数。</summary>
        public HeightPriorityConfiguration Config => _config;

        /// <summary>使用默认配置创建实例。</summary>
        public HeightPriorityStrategy() : this(new HeightPriorityConfiguration()) { }

        /// <inheritdoc />
        public Task<StrategyExecutionResult> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            var emptySeats = workspace.GetEmptySeats().ToList();
            if (emptySeats.Count == 0)
            {
                _logger.LogDebug("HeightPriority：无空座位，跳过");
                return Task.FromResult(new StrategyExecutionResult { Success = true });
            }

            var frontRowIds = SeatGeometryHelper.IdentifyFrontRowSeats(emptySeats, _config.FrontRowCount);
            var frontSeats = OrderSeats(emptySeats.Where(s => frontRowIds.Contains(s.Id))).ToList();
            if (frontSeats.Count == 0)
            {
                _logger.LogDebug("HeightPriority：未识别到前排座位，跳过");
                workspace.LogWarning(Id, DisplayNameConst, "HeightPriority_NoSeats");
                return Task.FromResult(new StrategyExecutionResult { Success = true });
            }

            var assignedIds = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();
            var candidates = workspace.Students
                .Where(s => !assignedIds.Contains(s.Id) && s.Height.HasValue)
                .OrderBy(s => s.Height!.Value)
                .ThenBy(s => s.Name, StringComparer.Ordinal)
                .ThenBy(s => s.Id, StringComparer.Ordinal)
                .ToList();

            if (candidates.Count == 0)
            {
                _logger.LogDebug("HeightPriority：无填写身高的未分配学生，跳过");
                workspace.LogInfo(Id, DisplayNameConst, "HeightPriority_NoHeightData");
                return Task.FromResult(new StrategyExecutionResult { Success = true });
            }

            int assignCount = Math.Min(frontSeats.Count, candidates.Count);
            int assigned = 0;
            for (int i = 0; i < assignCount && !cancellationToken.IsCancellationRequested; i++)
            {
                if (workspace.TryAssignSeat(frontSeats[i].Id, candidates[i].Id, out _))
                    assigned++;
            }

            _logger.LogInformation("HeightPriority 策略完成：前排 {FrontSeats} 个座位，按身高分配 {Assigned} 名学生",
                frontSeats.Count, assigned);
            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        /// <inheritdoc />
        public ValidationResult ValidateConfiguration()
            => _config.FrontRowCount >= 1
                ? new ValidationResult { IsValid = true }
                : new ValidationResult { IsValid = false, Error = "FrontRowCount must be at least 1." };

        /// <summary>
        /// 座位确定性排序：按类型（网格 → 环形 → 自由点）与几何坐标排列，
        /// 保证最矮的学生落在最靠前的位置。
        /// </summary>
        private static IEnumerable<Seat> OrderSeats(IEnumerable<Seat> seats) => seats
            .OrderBy(s => s switch { GridSeat => 0, PolarSeat => 1, FreeformSeat => 2, _ => 3 })
            .ThenBy(s => s switch
            {
                GridSeat g => g.Row,
                PolarSeat p => p.Ring,
                FreeformSeat f => f.Row ?? int.MaxValue,
                _ => int.MaxValue
            })
            .ThenBy(s => s switch
            {
                GridSeat g => (double)g.Column,
                PolarSeat p => p.AngleDegrees,
                FreeformSeat f => f.Y,
                _ => 0d
            })
            .ThenBy(s => s switch { FreeformSeat f => f.X, _ => 0d });

        /// <summary>
        /// 身高优先策略的配置参数。
        /// </summary>
        public class HeightPriorityConfiguration
        {
            /// <summary>前排行数（默认 1，最小 1）。</summary>
            public int FrontRowCount { get; set; } = 1;
        }
    }
}
