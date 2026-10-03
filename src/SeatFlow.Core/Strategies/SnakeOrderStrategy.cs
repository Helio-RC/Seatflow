using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Core.Strategies
{
    /// <summary>
    /// 蛇形顺序策略（Priority=10，在随机填充之前执行）。
    /// 按名单（工作区学生顺序）确定性填充空座：网格逐行蛇形（奇数行反向）、
    /// 环形逐环交替方向、自由点按行分组交替方向；关闭蛇形则逐行/逐环同向。
    /// 适合考场式排座等需要可预测顺序的场景。
    /// </summary>
    public class SnakeOrderStrategy(SnakeOrderStrategy.SnakeOrderConfiguration config, ILogger<SnakeOrderStrategy>? logger = null) : ISeatingStrategy
    {
        private readonly SnakeOrderConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
        private readonly ILogger<SnakeOrderStrategy> _logger = logger ?? NullLogger<SnakeOrderStrategy>.Instance;

        /// <summary>策略展示名称（与 manifest displayName 一致）。</summary>
        public const string DisplayNameConst = "蛇形顺序";

        /// <summary>策略 ID："SnakeOrder"。</summary>
        public string Id { get; } = "SnakeOrder";

        /// <summary>策略名称："SnakeOrder"。</summary>
        public string Name { get; } = "SnakeOrder";

        /// <summary>执行优先级：10（随机填充之前、其它位置类策略之后）。</summary>
        public int Priority { get; set; } = 10;

        /// <summary>是否启用（默认关闭，避免改变现有排座结果）。</summary>
        public bool IsEnabled { get; set; } = false;

        /// <summary>获取策略配置对象，供 Application 层读取和修改配置参数。</summary>
        public SnakeOrderConfiguration Config => _config;

        /// <summary>使用默认配置创建实例。</summary>
        public SnakeOrderStrategy() : this(new SnakeOrderConfiguration()) { }

        /// <inheritdoc />
        public Task<StrategyExecutionResult> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            var emptySeats = workspace.GetEmptySeats().ToList();
            var assignedIds = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();
            var unassigned = workspace.Students.Where(s => !assignedIds.Contains(s.Id)).ToList();

            if (emptySeats.Count == 0 || unassigned.Count == 0)
            {
                _logger.LogDebug("SnakeOrder：无空座位或未分配学生，跳过");
                return Task.FromResult(new StrategyExecutionResult { Success = true });
            }

            var orderedSeats = OrderSeats(emptySeats, _config.Serpentine).ToList();
            int assignCount = Math.Min(orderedSeats.Count, unassigned.Count);
            int assigned = 0;
            for (int i = 0; i < assignCount && !cancellationToken.IsCancellationRequested; i++)
            {
                if (workspace.TryAssignSeat(orderedSeats[i].Id, unassigned[i].Id, out _))
                    assigned++;
            }

            _logger.LogInformation("SnakeOrder 策略完成：按名单顺序蛇形分配 {Assigned} 名学生", assigned);
            workspace.LogInfo(Id, DisplayNameConst, "SnakeOrder_Assigned", assigned);
            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        /// <inheritdoc />
        public ValidationResult ValidateConfiguration() => new() { IsValid = true };

        /// <summary>按类型与几何坐标生成座位顺序；<paramref name="serpentine"/> 控制是否交替方向。</summary>
        private static IEnumerable<Seat> OrderSeats(IEnumerable<Seat> seats, bool serpentine)
        {
            var all = seats as IReadOnlyCollection<Seat> ?? seats.ToList();

            foreach (var seat in OrderGrid([.. all.OfType<GridSeat>()], serpentine))
                yield return seat;
            foreach (var seat in OrderPolar([.. all.OfType<PolarSeat>()], serpentine))
                yield return seat;
            foreach (var seat in OrderFreeform([.. all.OfType<FreeformSeat>()], serpentine))
                yield return seat;
        }

        private static IEnumerable<GridSeat> OrderGrid(List<GridSeat> seats, bool serpentine)
        {
            var rows = seats.Select(s => s.Row).Distinct().OrderBy(r => r).ToList();
            for (int i = 0; i < rows.Count; i++)
            {
                IEnumerable<GridSeat> rowSeats = seats.Where(s => s.Row == rows[i]).OrderBy(s => s.Column);
                if (serpentine && i % 2 == 1)
                    rowSeats = rowSeats.Reverse();
                foreach (var seat in rowSeats)
                    yield return seat;
            }
        }

        private static IEnumerable<PolarSeat> OrderPolar(List<PolarSeat> seats, bool serpentine)
        {
            var rings = seats.Select(s => s.Ring).Distinct().OrderBy(r => r).ToList();
            for (int i = 0; i < rings.Count; i++)
            {
                IEnumerable<PolarSeat> ringSeats = seats.Where(s => s.Ring == rings[i]).OrderBy(s => s.AngleDegrees);
                if (serpentine && i % 2 == 1)
                    ringSeats = ringSeats.Reverse();
                foreach (var seat in ringSeats)
                    yield return seat;
            }
        }

        private static IEnumerable<FreeformSeat> OrderFreeform(List<FreeformSeat> seats, bool serpentine)
        {
            var rows = seats.Select(s => s.Row).Distinct().OrderBy(r => r ?? int.MaxValue).ToList();
            for (int i = 0; i < rows.Count; i++)
            {
                IEnumerable<FreeformSeat> rowSeats = seats.Where(s => s.Row == rows[i]).OrderBy(s => s.Y).ThenBy(s => s.X);
                if (serpentine && i % 2 == 1)
                    rowSeats = rowSeats.Reverse();
                foreach (var seat in rowSeats)
                    yield return seat;
            }
        }

        /// <summary>
        /// 蛇形顺序策略的配置参数。
        /// </summary>
        public class SnakeOrderConfiguration
        {
            /// <summary>是否采用蛇形走向（true=奇数行/环反向，false=逐行/环同向）。</summary>
            public bool Serpentine { get; set; } = true;
        }
    }
}
