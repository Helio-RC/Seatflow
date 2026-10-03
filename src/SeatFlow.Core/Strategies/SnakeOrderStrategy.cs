using SeatFlow.Core.Enums;
using SeatFlow.Core.Models;
using SeatFlow.Core.Utilities;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Core.Strategies
{
    /// <summary>蛇形顺序的学生排序规则。</summary>
    public enum SnakeSortBy
    {
        /// <summary>名单顺序（数据集/工作区中的原始顺序）。</summary>
        Input,
        /// <summary>姓名自然序（汉字按拼音、数字按大小）。</summary>
        Name,
        /// <summary>身高（未填写身高的学生排在最后）。</summary>
        Height,
        /// <summary>性别（男 → 女 → 其他/未知）。</summary>
        Gender
    }

    /// <summary>蛇形顺序的起始方向。</summary>
    public enum SnakeDirection
    {
        /// <summary>正向（网格左→右、环形角度递增、自由点 X 递增）。</summary>
        Forward,
        /// <summary>反向（网格右→左、环形角度递减、自由点 X 递减）。</summary>
        Reverse
    }

    /// <summary>
    /// 蛇形顺序策略（Priority=10，在随机填充之前执行）。
    /// 按选定规则排序学生（默认名单顺序），并按选定走向确定性填充空座：
    /// 网格逐行蛇形（相邻行反向）、环形逐环交替方向、自由点按行分组交替方向；
    /// 可关闭蛇形（逐行同向）并设置起始方向（正向/反向）。
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

            var orderedSeats = OrderSeats(
                emptySeats, _config.Serpentine, _config.StartDirection == SnakeDirection.Reverse).ToList();
            var orderedStudents = OrderStudents(unassigned);

            int assignCount = Math.Min(orderedSeats.Count, orderedStudents.Count);
            int assigned = 0;
            for (int i = 0; i < assignCount && !cancellationToken.IsCancellationRequested; i++)
            {
                if (workspace.TryAssignSeat(orderedSeats[i].Id, orderedStudents[i].Id, out _))
                    assigned++;
            }

            _logger.LogInformation(
                "SnakeOrder 策略完成：排序规则 {SortBy}（{Direction}），蛇形 {Serpentine}，起始 {Start}，分配 {Assigned} 名学生",
                _config.SortBy, _config.SortDescending ? "降序" : "升序",
                _config.Serpentine, _config.StartDirection, assigned);
            workspace.LogInfo(Id, DisplayNameConst, "SnakeOrder_Assigned", assigned);
            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        /// <inheritdoc />
        public ValidationResult ValidateConfiguration() => new() { IsValid = true };

        /// <summary>按配置的规则与方向排序学生。</summary>
        private List<Student> OrderStudents(IEnumerable<Student> students)
        {
            var list = students.ToList();
            bool desc = _config.SortDescending;

            switch (_config.SortBy)
            {
                case SnakeSortBy.Name:
                {
                    var query = desc
                        ? list.OrderByDescending(s => s.Name, NaturalStringComparer.Instance)
                        : list.OrderBy(s => s.Name, NaturalStringComparer.Instance);
                    return [.. query.ThenBy(s => s.Id, StringComparer.Ordinal)];
                }
                case SnakeSortBy.Height:
                    return [.. list
                        .OrderBy(s => s.Height.HasValue ? 0 : 1)
                        .ThenBy(s => desc ? -(s.Height ?? 0f) : s.Height ?? 0f)
                        .ThenBy(s => s.Name, NaturalStringComparer.Instance)
                        .ThenBy(s => s.Id, StringComparer.Ordinal)];
                case SnakeSortBy.Gender:
                    return [.. list
                        .OrderBy(s => s.Gender is null or Gender.Unknown ? 1 : 0)
                        .ThenBy(s => desc ? -GenderRank(s.Gender) : GenderRank(s.Gender))
                        .ThenBy(s => s.Name, NaturalStringComparer.Instance)
                        .ThenBy(s => s.Id, StringComparer.Ordinal)];
                default: // Input：名单（工作区）顺序
                    if (desc)
                        list.Reverse();
                    return list;
            }
        }

        private static int GenderRank(Gender? gender) => gender switch
        {
            Gender.Male => 0,
            Gender.Female => 1,
            Gender.Other => 2,
            _ => 3
        };

        /// <summary>按类型与几何坐标生成座位顺序；<paramref name="serpentine"/> 控制是否交替方向，<paramref name="reverseStart"/> 控制起始方向。</summary>
        private static IEnumerable<Seat> OrderSeats(IEnumerable<Seat> seats, bool serpentine, bool reverseStart)
        {
            var all = seats as IReadOnlyCollection<Seat> ?? seats.ToList();

            foreach (var seat in OrderGrid([.. all.OfType<GridSeat>()], serpentine, reverseStart))
                yield return seat;
            foreach (var seat in OrderPolar([.. all.OfType<PolarSeat>()], serpentine, reverseStart))
                yield return seat;
            foreach (var seat in OrderFreeform([.. all.OfType<FreeformSeat>()], serpentine, reverseStart))
                yield return seat;
        }

        /// <summary>相邻组（行/环）是否反向：蛇形时交替，起始反向时整体取反。</summary>
        private static bool ShouldReverse(int groupIndex, bool serpentine, bool reverseStart)
            => (serpentine && groupIndex % 2 == 1) ^ reverseStart;

        private static IEnumerable<GridSeat> OrderGrid(List<GridSeat> seats, bool serpentine, bool reverseStart)
        {
            var rows = seats.Select(s => s.Row).Distinct().OrderBy(r => r).ToList();
            for (int i = 0; i < rows.Count; i++)
            {
                IEnumerable<GridSeat> rowSeats = seats.Where(s => s.Row == rows[i]).OrderBy(s => s.Column);
                if (ShouldReverse(i, serpentine, reverseStart))
                    rowSeats = rowSeats.Reverse();
                foreach (var seat in rowSeats)
                    yield return seat;
            }
        }

        private static IEnumerable<PolarSeat> OrderPolar(List<PolarSeat> seats, bool serpentine, bool reverseStart)
        {
            var rings = seats.Select(s => s.Ring).Distinct().OrderBy(r => r).ToList();
            for (int i = 0; i < rings.Count; i++)
            {
                IEnumerable<PolarSeat> ringSeats = seats.Where(s => s.Ring == rings[i]).OrderBy(s => s.AngleDegrees);
                if (ShouldReverse(i, serpentine, reverseStart))
                    ringSeats = ringSeats.Reverse();
                foreach (var seat in ringSeats)
                    yield return seat;
            }
        }

        private static IEnumerable<FreeformSeat> OrderFreeform(List<FreeformSeat> seats, bool serpentine, bool reverseStart)
        {
            var rows = seats.Select(s => s.Row).Distinct().OrderBy(r => r ?? int.MaxValue).ToList();
            for (int i = 0; i < rows.Count; i++)
            {
                IEnumerable<FreeformSeat> rowSeats = seats.Where(s => s.Row == rows[i]).OrderBy(s => s.Y).ThenBy(s => s.X);
                if (ShouldReverse(i, serpentine, reverseStart))
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
            /// <summary>学生排序规则（默认名单顺序）。</summary>
            public SnakeSortBy SortBy { get; set; } = SnakeSortBy.Input;

            /// <summary>排序是否降序（默认升序）。</summary>
            public bool SortDescending { get; set; }

            /// <summary>是否采用蛇形走向（true=相邻行/环反向，false=逐行/环同向）。</summary>
            public bool Serpentine { get; set; } = true;

            /// <summary>起始方向（默认正向）。</summary>
            public SnakeDirection StartDirection { get; set; } = SnakeDirection.Forward;
        }
    }
}
