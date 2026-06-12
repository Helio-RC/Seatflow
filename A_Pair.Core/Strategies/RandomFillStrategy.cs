using A_Pair.Core.Models;
using A_Pair.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Core.Strategies
{
    /// <summary>
    /// 随机填充策略（Priority=100，最后执行，填满所有剩余座位）。
    /// 作为管道中的兜底策略，在所有独立策略完成分配后运行。
    /// </summary>
    /// <remarks>
    /// <b>依赖策略上下文（v2）</b>
    /// <para>
    /// 当加载了 <see cref="IDependentSeatingStrategy"/> 时，RandomFill 的分配循环变为上下文驱动的：
    /// 每次随机选出 (student, seat) 对后，依次调用所有依赖策略进行评估。
    /// 依赖策略可以批准、拒绝（请求重掷）或自行完成分配（包括连携修改相邻座位）。
    /// 重掷有上限，超过后兜底强制分配。
    /// </para>
    /// <para>
    /// 若没有依赖策略，回退到原始的 Fisher-Yates 洗牌 + 顺序分配路径（快速路径）。
    /// </para>
    /// </remarks>
    public class RandomFillStrategy : ISeatingStrategy
    {
        private readonly Random _random;
        private readonly ILogger<RandomFillStrategy> _logger;
        private readonly List<IDependentSeatingStrategy> _dependentStrategies = [];

        /// <summary>每个 (student, seat) 对的最大重掷次数。</summary>
        private const int DefaultMaxRerolls = 10;

        public RandomFillStrategy (Random random , ILogger<RandomFillStrategy>? logger = null)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _logger = logger ?? NullLogger<RandomFillStrategy>.Instance;
        }

        /// <summary>
        /// 使用默认随机数种子创建实例。
        /// </summary>
        public RandomFillStrategy () : this(new Random()) { }

        /// <inheritdoc />
        public string Id { get; } = "RandomFill";

        /// <inheritdoc />
        public string Name { get; } = "RandomFill";

        /// <inheritdoc />
        public int Priority { get; set; } = 100;

        /// <inheritdoc />
        public bool IsEnabled { get; set; } = true;

        /// <summary>策略展示名称。</summary>
        public const string DisplayNameConst = "随机填充";

        /// <summary>
        /// 加载依赖策略列表。由 ApplicationFacade 在管道执行前调用。
        /// 依赖策略按 Priority 升序排列（数值越小越先评估）。
        /// </summary>
        public void LoadDependentStrategies (IEnumerable<IDependentSeatingStrategy> strategies)
        {
            _dependentStrategies.Clear();
            _dependentStrategies.AddRange(strategies.OrderBy(s => s.Priority));
            _logger.LogInformation("RandomFill：已加载 {Count} 个依赖策略" , _dependentStrategies.Count);
        }

        /// <summary>
        /// 是否有依赖策略已加载且至少一个启用。
        /// </summary>
        public bool HasActiveDependents => _dependentStrategies.Count > 0
            && _dependentStrategies.Any(s => s.IsEnabled);

        /// <inheritdoc />
        public async Task<StrategyExecutionResult> ExecuteAsync (
            SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            cancellationToken.ThrowIfCancellationRequested();

            var emptySeats = workspace.GetEmptySeats().ToList();
            var assignedIds = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();
            var unassignedStudents = workspace.Students
                .Where(s => !assignedIds.Contains(s.Id))
                .ToList();

            _logger.LogInformation(
                "RandomFill 策略开始执行：{EmptySeats} 个空座位，{Unassigned} 名未分配学生，{Dependent} 个依赖策略" ,
                emptySeats.Count , unassignedStudents.Count ,
                _dependentStrategies.Count(s => s.IsEnabled));

            if (unassignedStudents.Count == 0 || emptySeats.Count == 0)
            {
                _logger.LogDebug("RandomFill：无未分配学生或空座位，跳过");
                return new StrategyExecutionResult { Success = true };
            }

            // ── 快速路径：无依赖策略 → 原始 Fisher-Yates + 顺序分配 ──
            if (!HasActiveDependents)
            {
                FastPathAssign(workspace , unassignedStudents , emptySeats , cancellationToken);
                LogCompletion(workspace);
                return new StrategyExecutionResult { Success = true };
            }

            // ── 上下文路径：依赖策略参与评估 ──
            await ContextPathAssignAsync(workspace , emptySeats , unassignedStudents , cancellationToken);
            LogCompletion(workspace);
            return new StrategyExecutionResult { Success = true };
        }

        /// <summary>
        /// 快速路径：Fisher-Yates 洗牌 + 顺序分配。
        /// 无依赖策略时使用，避免上下文循环开销。
        /// </summary>
        private void FastPathAssign (
            SeatingWorkspace workspace ,
            List<Student> students ,
            List<Seat> seats ,
            CancellationToken ct)
        {
            // Fisher-Yates 洗牌
            for (int i = students.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (students[j] , students[i]) = (students[i] , students[j]);
            }

            int assignCount = Math.Min(seats.Count , students.Count);
            for (int i = 0; i < assignCount && !ct.IsCancellationRequested; i++)
            {
                workspace.TryAssignSeat(seats[i].Id , students[i].Id , out _);
            }

            _logger.LogInformation("RandomFill 快速路径完成：分配 {Assigned} 名学生" , assignCount);
        }

        /// <summary>
        /// 上下文路径：每次随机出 (student, seat) 对后调用依赖策略评估。
        /// 支持重掷和连携修改。
        /// </summary>
        private async Task ContextPathAssignAsync (
            SeatingWorkspace workspace ,
            List<Seat> initialEmptySeats ,
            List<Student> initialUnassigned ,
            CancellationToken ct)
        {
            var emptySeats = new List<Seat>(initialEmptySeats);
            var unassignedStudents = new List<Student>(initialUnassigned);
            var enabledDependents = _dependentStrategies.Where(s => s.IsEnabled).ToList();

            int maxRerolls = DefaultMaxRerolls;
            int totalRerolls = 0;

            while (emptySeats.Count > 0 && unassignedStudents.Count > 0 && !ct.IsCancellationRequested)
            {
                // 随机选一个学生
                var student = unassignedStudents[_random.Next(unassignedStudents.Count)];

                // 随机选一个座位
                var seat = emptySeats[_random.Next(emptySeats.Count)];

                int rerollCount = 0;
                bool assigned = false;

                while (rerollCount < maxRerolls && !ct.IsCancellationRequested)
                {
                    var context = new RandomFillContextImpl(workspace , rerollCount , maxRerolls);
                    bool alreadyHandled = false;
                    bool allApproved = true;

                    // 依次调用依赖策略评估
                    foreach (var dep in enabledDependents)
                    {
                        ct.ThrowIfCancellationRequested();

                        var result = await dep.EvaluateAsync(
                            workspace , student , seat , context , ct);

                        if (!result.Approved)
                        {
                            // 策略拒绝，触发重掷
                            rerollCount++;
                            totalRerolls++;
                            allApproved = false;

                            // 选一个不同的座位重试
                            var otherSeats = emptySeats
                                .Where(s => s.Id != seat.Id)
                                .ToList();
                            if (otherSeats.Count == 0)
                            {
                                // 没有其他座位可选，强制退出重试循环
                                rerollCount = maxRerolls;
                            }
                            else
                            {
                                seat = otherSeats[_random.Next(otherSeats.Count)];
                            }

                            _logger.LogDebug(
                                "RandomFill：策略 {StrategyId} 拒绝 ({Reason})，重掷 #{Reroll}/{Max}，学生 {Student}",
                                dep.Id , result.Message ?? "无原因" ,
                                rerollCount , maxRerolls , student.Name);
                            break; // 重新开始依赖策略评估循环
                        }

                        if (result.AlreadyHandled)
                        {
                            // 依赖策略已自行完成分配（含连携修改）
                            alreadyHandled = true;
                            _logger.LogDebug(
                                "RandomFill：策略 {StrategyId} 已处理分配，学生 {Student} → {Seat}" ,
                                dep.Id , student.Name , seat.Id);
                            break; // 不再检查后续依赖策略
                        }
                    }

                    if (allApproved)
                    {
                        // 所有依赖策略批准，执行分配
                        if (!alreadyHandled)
                        {
                            workspace.TryAssignSeat(seat.Id , student.Id , out _);
                        }
                        else
                        {
                            // 安全检查：依赖策略声称已处理，但学生可能未实际分配
                            // 这种情况下补调 TryAssignSeat 防止无限循环
                            var alreadyAssigned = workspace.BuildSeatingPlan()
                                .Assignments.Values.Contains(student.Id);
                            if (!alreadyAssigned)
                            {
                                _logger.LogWarning(
                                    "RandomFill：策略声称已处理但学生 {Student} 未分配，执行回退分配" ,
                                    student.Name);
                                workspace.TryAssignSeat(seat.Id , student.Id , out _);
                            }
                        }

                        // 刷新状态：依赖策略可能已分配多名学生（连携修改）
                        emptySeats = workspace.GetEmptySeats().ToList();
                        var currentAssigned = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();
                        unassignedStudents = workspace.Students
                            .Where(s => !currentAssigned.Contains(s.Id))
                            .ToList();

                        assigned = true;
                        break;
                    }
                }

                if (!assigned)
                {
                    // 重掷上限：兜底强制分配
                    _logger.LogWarning(
                        "RandomFill：重掷次数达上限 {MaxRerolls}，学生 {Student} 强制分配到座位 {Seat}" ,
                        maxRerolls , student.Name , seat.Id);

                    workspace.LogWarning(
                        Id , DisplayNameConst , "RandomFill_RerollExhausted" ,
                        maxRerolls , student.Id , seat.Id);

                    workspace.TryAssignSeat(seat.Id , student.Id , out _);

                    emptySeats = workspace.GetEmptySeats().ToList();
                    var currentAssigned = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();
                    unassignedStudents = workspace.Students
                        .Where(s => !currentAssigned.Contains(s.Id))
                        .ToList();
                }
            }

            _logger.LogInformation(
                "RandomFill 上下文路径完成：剩余空座 {RemainingSeats}，剩余学生 {RemainingStudents}，总重掷 {TotalRerolls} 次" ,
                emptySeats.Count , unassignedStudents.Count , totalRerolls);
        }

        private void LogCompletion (SeatingWorkspace workspace)
        {
            var remainingSeats = workspace.GetEmptySeats().Count();
            var remainingStudents = workspace.Students.Count(s =>
                !workspace.BuildSeatingPlan().Assignments.Values.Contains(s.Id));
            _logger.LogInformation(
                "RandomFill 策略完成：{Unfilled} 个座位空置，{Unassigned} 名学生未分配" ,
                remainingSeats , remainingStudents);
        }

        /// <inheritdoc />
        public ValidationResult ValidateConfiguration () => new() { IsValid = true };

        // ═══════════════ RandomFillContext 内部实现 ═══════════════

        /// <summary>
        /// <see cref="IRandomFillContext"/> 的内部实现，代理日志调用到 workspace。
        /// </summary>
        private sealed class RandomFillContextImpl : IRandomFillContext
        {
            private readonly SeatingWorkspace _workspace;

            public RandomFillContextImpl (SeatingWorkspace workspace , int rerollCount , int maxRerolls)
            {
                _workspace = workspace;
                RerollCount = rerollCount;
                MaxRerolls = maxRerolls;
            }

            /// <inheritdoc />
            public int RerollCount { get; }

            /// <inheritdoc />
            public int MaxRerolls { get; }

            /// <inheritdoc />
            public void LogWarning (string strategyId , string displayName , string messageKey , params object?[] args)
                => _workspace.LogWarning(strategyId , displayName , messageKey , args);

            /// <inheritdoc />
            public void LogError (string strategyId , string displayName , string messageKey , params object?[] args)
                => _workspace.LogError(strategyId , displayName , messageKey , args);
        }
    }
}
