using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Core.Strategies
{
    /// <summary>
    /// 不为同桌策略（依赖策略，Priority=42，在 RandomFill 上下文中执行）。
    /// 配置若干搭配组；同一组内的任意两名学生不安排在同一张课桌（同桌）。
    /// 当 RandomFill 提议的分配会让同组伙伴成为同桌时请求重掷；
    /// 重掷次数耗尽后强制分配并记录警告。
    /// </summary>
    /// <remarks>
    /// 组内成员自动去重；同一学生可出现在多个组中（约束按并集生效）。
    /// 同桌判定使用 <see cref="SeatAdjacencyHelper.AreDeskMates"/>（同行+邻列+同每桌座位数）。
    /// </remarks>
    public class NoDeskMateStrategy(
        NoDeskMateStrategy.NoDeskMateConfiguration config,
        ILogger<NoDeskMateStrategy>? logger = null) : IDependentSeatingStrategy
    {
        private readonly NoDeskMateConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
        private readonly ILogger<NoDeskMateStrategy> _logger = logger ?? NullLogger<NoDeskMateStrategy>.Instance;

        /// <summary>每桌座位数（来自会场 GridLayoutMetadata.SeatsPerDesk），用于桌边界判定。</summary>
        private int _seatsPerDesk = 2;

        /// <summary>策略展示名称（与 manifest displayName 一致）。</summary>
        public const string DisplayNameConst = "不为同桌";

        /// <inheritdoc />
        public string Id { get; } = "NoDeskMate";

        /// <inheritdoc />
        public string Name { get; } = "NoDeskMate";

        /// <inheritdoc />
        public string DisplayName => DisplayNameConst;

        /// <inheritdoc />
        public int Priority { get; set; } = 42;

        /// <inheritdoc />
        public bool IsEnabled { get; set; } = false;

        /// <summary>获取策略配置对象，供 Application 层读取和修改配置参数。</summary>
        public NoDeskMateConfiguration Config => _config;

        /// <summary>使用默认配置创建实例。</summary>
        public NoDeskMateStrategy() : this(new NoDeskMateConfiguration()) { }

        /// <summary>同步会场每桌座位数，用于桌边界检查。</summary>
        public void SetSeatsPerDesk(int count) => _seatsPerDesk = Math.Max(1, count);

        /// <summary>
        /// 设置搭配组。组内自动去重；少于 2 名有效学生的组被忽略。
        /// 由 Application 层在管道执行前从配置行转换后调用。
        /// </summary>
        public void SetGroups(IEnumerable<IEnumerable<string>> groups)
        {
            _config.Groups.Clear();
            foreach (var group in groups)
            {
                var set = new HashSet<string>(
                    group.Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
                if (set.Count >= 2)
                    _config.Groups.Add(set);
            }
        }

        /// <summary>清空所有搭配组。</summary>
        public void ClearGroups() => _config.Groups.Clear();

        /// <inheritdoc />
        public Task<DependentEvaluationResult> EvaluateAsync(
            SeatingWorkspace workspace,
            Student student,
            Seat targetSeat,
            IRandomFillContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            ArgumentNullException.ThrowIfNull(student);
            ArgumentNullException.ThrowIfNull(targetSeat);
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            // 固定座位由 FixedSeatStrategy 全权负责，不干涉
            if (targetSeat.IsFixed)
                return Task.FromResult(DependentResult.Approve());

            if (_config.Groups.Count == 0)
                return Task.FromResult(DependentResult.Approve());

            // 该学生所属的搭配组（同一学生可属于多个组）
            var studentGroups = _config.Groups.Where(g => g.Contains(student.Id)).ToList();
            if (studentGroups.Count == 0)
                return Task.FromResult(DependentResult.Approve());

            var deskMateSeats = workspace.FindSeats(s =>
                s.Id != targetSeat.Id
                && !s.IsAvailable
                && s.OccupantId is not null
                && !s.IsFixed
                && SeatAdjacencyHelper.AreDeskMates(s, targetSeat, _seatsPerDesk));

            foreach (var mateSeat in deskMateSeats)
            {
                var mateId = mateSeat.OccupantId!;
                if (!studentGroups.Any(g => g.Contains(mateId)))
                    continue;

                if (context.RerollCount < context.MaxRerolls - 1)
                {
                    _logger.LogDebug(
                        "NoDeskMate：学生 {Student} 的目标座位 {Seat} 同桌 {Mate} 属于同一搭配组，请求重掷",
                        student.Name, targetSeat.Id, mateId);
                    return Task.FromResult(DependentResult.Reject());
                }

                context.LogWarning(Id, DisplayNameConst, "NoDeskMate_Forced", student.Id, mateId);
                _logger.LogInformation(
                    "NoDeskMate：重掷耗尽，学生 {Student} 强制分配到 {Seat}，与同组伙伴 {Mate} 成为同桌",
                    student.Name, targetSeat.Id, mateId);
                return Task.FromResult(DependentResult.Approve());
            }

            return Task.FromResult(DependentResult.Approve());
        }

        /// <inheritdoc />
        public ValidationResult ValidateConfiguration() => new() { IsValid = true };

        /// <summary>
        /// 不为同桌策略的配置。每个元素为一个搭配组（组内学生 ID 集合）。
        /// </summary>
        public class NoDeskMateConfiguration
        {
            /// <summary>搭配组列表（组内任意两人不互为同桌）。</summary>
            public List<HashSet<string>> Groups { get; } = [];
        }
    }
}
