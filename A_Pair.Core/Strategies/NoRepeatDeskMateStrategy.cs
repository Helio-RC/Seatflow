using A_Pair.Core.Models;
using A_Pair.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Core.Strategies
{
    /// <summary>
    /// 同桌不重复策略（依赖策略，在 RandomFill 上下文中执行）。
    /// 当 RandomFill 随机选出 (student, seat) 对时，此策略检查该座位相邻的已占座位中
    /// 是否有该学生过去的同桌。若有则请求重掷以尝试其他座位；
    /// 若重掷次数耗尽则强制分配并记录警告。
    /// </summary>
    /// <remarks>
    /// <b>与 DeskMate 的关系</b>
    /// <para>
    /// 本策略优先级（60）高于 DeskMate（50），先于 DeskMate 执行。
    /// 所有学生（含 DeskMate 组成员）都会先经过重复检查。
    /// DeskMate 返回 Handled 时会跳过后续依赖策略（含本策略），
    /// 这是 Handled 机制的已知限制——组成员内部排列由 DeskMate 全权负责。
    /// </para>
    /// <para>
    /// <b>同桌定义</b>
    /// 使用 <see cref="SeatAdjacencyHelper.AreDeskMates"/> 进行桌边界感知的邻接判定。
    /// 历史提取也使用相同的邻接逻辑，确保判定一致。
    /// </para>
    /// </remarks>
    public class NoRepeatDeskMateStrategy : IDependentSeatingStrategy
    {
        private readonly NoRepeatDeskMateConfiguration _config;
        private readonly ILogger<NoRepeatDeskMateStrategy> _logger;

        /// <summary>
        /// 过去的同桌对集合。存储规范化后的 (smallerId, largerId) 元组，
        /// 支持双向 O(1) 查找。
        /// </summary>
        private readonly HashSet<(string A , string B)> _pastDeskMatePairs = [];

        /// <summary>每桌座位数（来自会场 GridLayoutMetadata.SeatsPerDesk），用于桌边界判定。</summary>
        private int _seatsPerDesk = 2;

        /// <summary>策略展示名称（与 manifest displayName 一致）。</summary>
        public const string DisplayNameConst = "同桌不重复";

        /// <inheritdoc />
        public string Id { get; } = "NoRepeatDeskMate";

        /// <inheritdoc />
        public string Name { get; } = "NoRepeatDeskMate";

        /// <inheritdoc />
        public string DisplayName => DisplayNameConst;

        /// <inheritdoc />
        public int Priority { get; set; } = 60;

        /// <inheritdoc />
        public bool IsEnabled { get; set; } = true;

        /// <summary>获取策略配置对象。</summary>
        public NoRepeatDeskMateConfiguration Config => _config;

        public NoRepeatDeskMateStrategy (
            NoRepeatDeskMateConfiguration config ,
            ILogger<NoRepeatDeskMateStrategy>? logger = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? NullLogger<NoRepeatDeskMateStrategy>.Instance;
        }

        /// <summary>使用默认配置创建实例。</summary>
        public NoRepeatDeskMateStrategy () : this(new NoRepeatDeskMateConfiguration()) { }

        /// <summary>同步会场每桌座位数，用于桌边界检查。</summary>
        public void SetSeatsPerDesk (int count) => _seatsPerDesk = Math.Max(1 , count);

        /// <summary>
        /// 设置过去的同桌对。由 <c>NoRepeatDeskMateHistoryLoader</c> 在管道执行前调用。
        /// 自动规范化为 (smallerId, largerId) 以支持双向查找。
        /// </summary>
        public void SetPastDeskMatePairs (IEnumerable<(string StudentA , string StudentB)> pairs)
        {
            _pastDeskMatePairs.Clear();
            foreach (var (a , b) in pairs)
                _pastDeskMatePairs.Add(NormalizePair(a , b));
        }

        /// <summary>清空所有历史同桌对。</summary>
        public void ClearHistory () => _pastDeskMatePairs.Clear();

        /// <inheritdoc />
        public Task<DependentEvaluationResult> EvaluateAsync (
            SeatingWorkspace workspace ,
            Student student ,
            Seat targetSeat ,
            IRandomFillContext context ,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            ArgumentNullException.ThrowIfNull(student);
            ArgumentNullException.ThrowIfNull(targetSeat);
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            var result = Evaluate(workspace , student , targetSeat , context);
            return Task.FromResult(result);
        }

        private DependentEvaluationResult Evaluate (
            SeatingWorkspace workspace ,
            Student student ,
            Seat targetSeat ,
            IRandomFillContext context)
        {
            // 1. 无历史数据 → 放行
            if (_pastDeskMatePairs.Count == 0)
                return DependentResult.Approve();

            // 2. 收集该学生的历史同桌 ID
            var pastMates = new List<string>();
            foreach (var (a , b) in _pastDeskMatePairs)
            {
                if (a == student.Id) pastMates.Add(b);
                else if (b == student.Id) pastMates.Add(a);
            }
            if (pastMates.Count == 0)
                return DependentResult.Approve();

            // 3. 查找 targetSeat 的相邻已占座位（桌边界感知）
            // 注意：排除固定座位（IsFixed=true），因为固定座位由教师手动指定，
            // 其占位者不受同桌不重复策略约束。
            var adjacentOccupied = workspace.FindSeats(s =>
                s.Id != targetSeat.Id
                && !s.IsAvailable
                && s.OccupantId is not null
                && !s.IsFixed
                && SeatAdjacencyHelper.AreDeskMates(s , targetSeat , _seatsPerDesk));

            // 4. 检查是否有历史同桌在相邻座位
            foreach (var occSeat in adjacentOccupied)
            {
                if (!pastMates.Contains(occSeat.OccupantId!))
                    continue;

                // 发现重复同桌
                if (context.RerollCount < context.MaxRerolls - 1)
                {
                    _logger.LogDebug(
                        "NoRepeatDeskMate：学生 {Student} 的目标座位 {Seat} 旁有过去的同桌 {Mate}，请求重掷" ,
                        student.Name , targetSeat.Id , occSeat.OccupantId);
                    return DependentResult.Reject();
                }

                // 重掷已耗尽——强制分配并警告
                context.LogWarning(
                    Id , DisplayNameConst , "NoRepeatDeskMate_Forced" ,
                    student.Id , occSeat.OccupantId!);
                _logger.LogInformation(
                    "NoRepeatDeskMate：重掷耗尽，学生 {Student} 强制分配到 {Seat}，与过去同桌 {Mate} 相邻" ,
                    student.Name , targetSeat.Id , occSeat.OccupantId);
                return DependentResult.Approve();
            }

            // 5. 无冲突
            return DependentResult.Approve();
        }

        /// <inheritdoc />
        public ValidationResult ValidateConfiguration () => new() { IsValid = true };

        /// <summary>
        /// 规范化学生对，确保 (A, B) 和 (B, A) 映射到相同的哈希。
        /// </summary>
        private static (string , string) NormalizePair (string a , string b)
            => string.CompareOrdinal(a , b) <= 0 ? (a , b) : (b , a);
    }

    /// <summary>
    /// 同桌不重复策略的配置。
    /// </summary>
    public class NoRepeatDeskMateConfiguration
    {
        /// <summary>参考历史快照个数（默认 10，范围 1–30）。</summary>
        public int HistoryWindowSize { get; set; } = 10;
    }
}
