using A_Pair.Core.Models;
using A_Pair.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Core.Strategies
{
    /// <summary>
    /// 同桌组策略（依赖策略，在 RandomFill 上下文中执行）。
    /// 当 RandomFill 随机选出 (student, seat) 对时，此策略检查该学生是否有同桌组。
    /// 若有，则尝试将同组学生分配到相邻座位（连携修改）。
    /// 若目标座位没有足够相邻空座，则请求重掷（Reroll）以尝试其他座位。
    /// </summary>
    /// <remarks>
    /// <b>与旧版的关键区别</b>
    /// <para>
    /// 旧版作为独立策略，在 FixedSeat 和 FrontRowRotation 之后执行，此时座位网格已严重碎片化，
    /// 连续座位块几乎不存在，导致成功率极低。新版作为依赖策略嵌入 RandomFill 的分配循环中，
    /// 在随机填充过程中实时检测同桌关系并协调分配，不再依赖预先存在的连续块。
    /// </para>
    /// </remarks>
    public class DeskMateStrategy : IDependentSeatingStrategy
    {
        private readonly DeskMateConfiguration _config;
        private readonly ILogger<DeskMateStrategy> _logger;
        private readonly Random _random;

        /// <summary>获取策略配置对象，供 Application 层读取和修改配置参数。</summary>
        public DeskMateConfiguration Config => _config;

        public DeskMateStrategy (DeskMateConfiguration config , ILogger<DeskMateStrategy>? logger = null , Random? random = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? NullLogger<DeskMateStrategy>.Instance;
            _random = random ?? new Random();
        }

        /// <summary>使用默认配置创建实例。</summary>
        public DeskMateStrategy () : this(new DeskMateConfiguration()) { }

        /// <summary>策略展示名称（与 manifest displayName 一致）。</summary>
        public const string DisplayNameConst = "同桌分组";

        /// <inheritdoc />
        public string Id { get; } = "DeskMate";

        /// <inheritdoc />
        public string Name { get; } = "DeskMate";

        /// <inheritdoc />
        public string DisplayName => DisplayNameConst;

        /// <inheritdoc />
        public int Priority { get; set; } = 50;

        /// <inheritdoc />
        public bool IsEnabled { get; set; } = true;

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
            // 获取当前已分配的学生 ID
            var assignedIds = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();

            // 查找该学生所属的同桌组
            var group = FindGroupForStudent(student , assignedIds);
            if (group is null)
                return DependentResult.Approve();

            // 从配置中的原始组获取已分配的组员（用于 near-occupied 检测）
            var originalGroup = _config.Groups.FirstOrDefault(g => g.StudentIds.Contains(student.Id));
            var preAssignedMates = originalGroup?.StudentIds
                .Where(id => id != student.Id && assignedIds.Contains(id))
                .ToList() ?? [];

            // 从未过滤的组中获取未分配的组员
            var unassignedMates = group.StudentIds
                .Where(id => id != student.Id && !assignedIds.Contains(id))
                .ToList();

            _logger.LogDebug(
                "DeskMate 评估：学生 {Student}，目标座位 {Seat}，组大小 {GroupSize}，未分配 {Unassigned}，已分配 {PreAssigned}" ,
                student.Name , targetSeat.Id , group.StudentIds.Count , unassignedMates.Count , preAssignedMates.Count);

            // ── 场景1：有组员已被前序策略分配（near-occupied） ──
            if (preAssignedMates.Count > 0)
            {
                return HandleNearOccupied(
                    workspace , student , targetSeat , preAssignedMates , context);
            }

            // ── 场景2：所有组员均未分配（coordinated assignment） ──
            if (unassignedMates.Count > 0)
            {
                return HandleCoordinatedAssignment(
                    workspace , student , targetSeat , unassignedMates , context);
            }

            // ── 场景3：该学生是组内唯一未分配成员（其余均已在之前迭代中分配） ──
            // 这种情况可能发生在之前的 EvaluateAsync 调用已处理了其他组员
            // 直接批准，由 RandomFill 正常分配
            return DependentResult.Approve();
        }

        /// <summary>
        /// 处理 near-occupied 场景：部分组员已被前序策略（FixedSeat/FrontRowRotation）分配。
        /// 若 targetSeat 邻接已分配组员的座位 → 批准。
        /// 否则尝试在已分配组员座位旁找空座 → 切换分配（Handled）。
        /// </summary>
        private DependentEvaluationResult HandleNearOccupied (
            SeatingWorkspace workspace ,
            Student student ,
            Seat targetSeat ,
            List<string> preAssignedMates ,
            IRandomFillContext context)
        {
            // 获取已分配组员占用的座位
            var occupiedSeats = workspace.FindSeats(
                s => s.OccupantId is not null && preAssignedMates.Contains(s.OccupantId)).ToList();

            if (occupiedSeats.Count == 0)
            {
                // 理论上不应发生（已分配学生应有座位），容错处理
                _logger.LogWarning("DeskMate：已分配组员未找到座位，批准当前分配");
                return DependentResult.Approve();
            }

            // 检查 targetSeat 是否与任何组员的座位相邻
            bool isAdjacent = occupiedSeats.Any(occ => AreSeatsAdjacent(occ , targetSeat));
            if (isAdjacent)
            {
                _logger.LogDebug("DeskMate：目标座位 {SeatId} 与已分配组员相邻，批准" , targetSeat.Id);
                return DependentResult.Approve();
            }

            // targetSeat 不相邻——尝试在已分配组员座位周围找空座
            var emptySeats = workspace.GetEmptySeats().ToList();
            var candidates = new List<Seat>();
            foreach (var occSeat in occupiedSeats)
            {
                var nearby = emptySeats.Where(e => AreSeatsAdjacent(occSeat , e)).ToList();
                candidates.AddRange(nearby);
            }

            if (candidates.Count > 0)
            {
                var chosen = candidates[_random.Next(candidates.Count)];
                if (workspace.TryAssignSeat(chosen.Id , student.Id , out _))
                {
                    _logger.LogInformation("DeskMate：将学生 {Student} 分配到组员邻座 {SeatId}（原提议 {OriginalSeat} 不邻接）" ,
                        student.Name , chosen.Id , targetSeat.Id);
                    workspace.LogWarning(Id , DisplayNameConst , "DeskMate_NearOccupied" , student.Id , chosen.Id);
                    return DependentResult.Handled();
                }
            }

            // 无相邻空座可用，请求重掷
            _logger.LogDebug("DeskMate：学生 {Student} 无法在已分配组员旁找到空座，请求重掷" , student.Name);
            return DependentResult.Reject();
        }

        /// <summary>
        /// 处理协调分配场景：所有组员均未分配。
        /// 检查 targetSeat 周围是否有足够相邻空座容纳全部组员。
        /// 若足够 → 同时分配 student + 组员（Handled）。
        /// 若不足 → 请求重掷（Reject）。
        /// </summary>
        private DependentEvaluationResult HandleCoordinatedAssignment (
            SeatingWorkspace workspace ,
            Student student ,
            Seat targetSeat ,
            List<string> unassignedMates ,
            IRandomFillContext context)
        {
            int neededAdjacent = unassignedMates.Count;

            // 收集 targetSeat 周围的空座位
            var emptySeats = workspace.GetEmptySeats()
                .Where(s => s.Id != targetSeat.Id)
                .ToList();
            var adjacentEmpty = FindAdjacentEmptySeats(targetSeat , emptySeats);

            if (adjacentEmpty.Count < neededAdjacent)
            {
                // 相邻空座不足，请求重掷
                _logger.LogDebug(
                    "DeskMate：目标座位 {SeatId} 周围仅有 {Adjacent} 个相邻空座，需要 {Needed} 个，请求重掷" ,
                    targetSeat.Id , adjacentEmpty.Count , neededAdjacent);

                // 仅在首次和接近上限时记录警告，避免刷屏
                if (context.RerollCount == 0 || context.RerollCount >= context.MaxRerolls - 1)
                {
                    context.LogWarning(Id , DisplayNameConst , "DeskMate_RerollNoAdjacent" ,
                        student.Id , neededAdjacent , adjacentEmpty.Count);
                }

                return DependentResult.Reject();
            }

            // 随机打乱相邻空座，优先取前 N 个，剩余作为回退池
            Shuffle(adjacentEmpty , _random);
            var mateSeats = adjacentEmpty.Take(neededAdjacent).ToList();
            var fallbackPool = adjacentEmpty.Skip(neededAdjacent).ToList();

            // 先分配 student 到 targetSeat
            if (!workspace.TryAssignSeat(targetSeat.Id , student.Id , out var err))
            {
                _logger.LogWarning("DeskMate：分配学生 {Student} 到 {Seat} 失败：{Error}" ,
                    student.Name , targetSeat.Id , err);
                return DependentResult.Reject();
            }

            // 将组员分配到相邻空座；失败时尝试回退池中的其他相邻座位
            int assignedMates = 0;
            var unassignedIds = new List<string>(unassignedMates);
            Shuffle(unassignedIds , _random);

            for (int i = 0; i < unassignedIds.Count; i++)
            {
                bool assigned = false;
                if (i < mateSeats.Count)
                {
                    assigned = workspace.TryAssignSeat(mateSeats[i].Id , unassignedIds[i] , out _);
                }
                // 失败或座位不够时尝试回退池
                if (!assigned && fallbackPool.Count > 0)
                {
                    for (int fb = 0; fb < fallbackPool.Count; fb++)
                    {
                        if (fallbackPool[fb].IsAvailable && workspace.TryAssignSeat(fallbackPool[fb].Id , unassignedIds[i] , out _))
                        {
                            assigned = true;
                            fallbackPool.RemoveAt(fb);
                            break;
                        }
                    }
                }
                if (assigned) assignedMates++;
            }

            _logger.LogInformation(
                "DeskMate：协调分配完成——学生 {Student} → {Seat}，组员 {Assigned}/{Total} 人" ,
                student.Name , targetSeat.Id , assignedMates , unassignedMates.Count);

            if (assignedMates < unassignedMates.Count)
            {
                context.LogWarning(Id , DisplayNameConst , "DeskMate_PartialAssign" ,
                    student.Id , assignedMates , unassignedMates.Count);
            }

            return DependentResult.Handled();
        }

        /// <summary>
        /// 查找指定座位周围的相邻空座，根据配置过滤方向偏好。
        /// </summary>
        /// <param name="seat">参考座位。</param>
        /// <param name="emptySeats">空座位列表。</param>
        /// <returns>与参考座位相邻的空座位列表（未排序）。</returns>
        private List<Seat> FindAdjacentEmptySeats (Seat seat , List<Seat> emptySeats)
        {
            var result = new List<Seat>();
            foreach (var empty in emptySeats)
            {
                if (IsAdjacentWithConfig(seat , empty))
                    result.Add(empty);
            }
            return result;
        }

        /// <summary>
        /// 根据配置偏好判断两个座位是否相邻。
        /// <see cref="DeskMateConfiguration.PreferHorizontal"/> 和 <see cref="DeskMateConfiguration.AllowVertical"/>
        /// 控制 Grid 布局中的方向性。
        /// </summary>
        private bool IsAdjacentWithConfig (Seat a , Seat b)
        {
            // 非 Grid 布局使用通用 adjacency 判定（LogicalGroup / 几何距离）
            if (a is not GridSeat ga || b is not GridSeat gb)
                return AreSeatsAdjacent(a , b);

            bool horizontalOk = _config.PreferHorizontal
                && ga.Row == gb.Row && Math.Abs(ga.Column - gb.Column) == 1;
            bool verticalOk = _config.AllowVertical
                && ga.Column == gb.Column && Math.Abs(ga.Row - gb.Row) == 1;

            return horizontalOk || verticalOk;
        }

        /// <summary>
        /// 查找学生所属的同桌组。
        /// 优先从 <see cref="DeskMateConfiguration.Groups"/> 查找，
        /// 其次从 Student.Extensions["DeskMates"] 查找（向后兼容）。
        /// </summary>
        private DeskMateGroup? FindGroupForStudent (Student student , HashSet<string> assignedIds)
        {
            // 先从配置中的组查找
            foreach (var group in _config.Groups)
            {
                if (group.StudentIds.Contains(student.Id))
                {
                    // 过滤掉已分配的学生（但不排除当前学生本身）
                    var activeIds = group.StudentIds
                        .Where(id => id == student.Id || !assignedIds.Contains(id))
                        .ToList();
                    if (activeIds.Count >= 1)
                        return new DeskMateGroup { GroupId = group.GroupId , StudentIds = activeIds };
                }
            }

            // 从 Extensions["DeskMates"] 查找（向后兼容）
            if (student.Extensions.TryGet<List<string>>("DeskMates" , out var mates) && mates is { Count: > 0 })
            {
                var groupIds = new List<string> { student.Id };
                foreach (var mate in mates)
                {
                    if (!assignedIds.Contains(mate) && !groupIds.Contains(mate))
                        groupIds.Add(mate);
                }
                if (groupIds.Count >= 2)
                    return new DeskMateGroup { StudentIds = groupIds };
            }

            return null;
        }

        /// <summary>
        /// 判断两个座位是否相邻。
        /// 网格座位：同行左右相邻或同列上下相邻。
        /// 极坐标座位：同环角度差 ≤45°，或相邻环角度几乎相同。
        /// 自由点座位：欧几里得距离 ≤1.5 或同 LogicalGroup。
        /// 混合类型座位不视为相邻。
        /// </summary>
        private bool AreSeatsAdjacent (Seat a , Seat b)
        {
            if (a is GridSeat ga && b is GridSeat gb)
            {
                return (ga.Row == gb.Row && Math.Abs(ga.Column - gb.Column) == 1)
                    || (ga.Column == gb.Column && Math.Abs(ga.Row - gb.Row) == 1);
            }

            if (a is PolarSeat pa && b is PolarSeat pb)
            {
                // 优先：LogicalGroup 判定（由布局构建器设置，反映通道划分）
                bool hasGroups = !string.IsNullOrEmpty(pa.LogicalGroup)
                              && !string.IsNullOrEmpty(pb.LogicalGroup);
                if (hasGroups)
                    return pa.LogicalGroup == pb.LogicalGroup;

                // 回退：几何判定（无 LogicalGroup 的旧数据）
                const double angleTolerance = 1e-6;
                bool sameRing = Math.Abs(pa.Radius - pb.Radius) < 1e-6;
                if (sameRing)
                {
                    double raw = Math.Abs(pa.AngleDegrees - pb.AngleDegrees);
                    double angleDiff = Math.Min(raw , 360.0 - raw);
                    if (angleDiff <= 45.0) return true;
                }
                else
                {
                    double raw = Math.Abs(pa.AngleDegrees - pb.AngleDegrees);
                    double angleDiff = Math.Min(raw , 360.0 - raw);
                    if (angleDiff < angleTolerance)
                        return true;
                }
                return false;
            }

            if (a is FreeformSeat fa && b is FreeformSeat fb)
            {
                // 优先：LogicalGroup 判定（由布局构建器设置，反映分组划分）
                bool hasGroups = !string.IsNullOrEmpty(fa.LogicalGroup)
                              && !string.IsNullOrEmpty(fb.LogicalGroup);
                if (hasGroups)
                    return fa.LogicalGroup == fb.LogicalGroup;

                // 回退：欧几里得距离判定
                double dx = fa.X - fb.X;
                double dy = fa.Y - fb.Y;
                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                return distance <= 1.5;
            }

            return false;
        }

        /// <summary>
        /// Fisher-Yates 洗牌算法。
        /// </summary>
        private static void Shuffle<T> (IList<T> list , Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[j] , list[i]) = (list[i] , list[j]);
            }
        }

        /// <summary>
        /// 验证配置：Groups 不能为 null，每个组至少包含 2 名学生。
        /// </summary>
        public ValidationResult ValidateConfiguration ()
        {
            if (_config.Groups == null)
            {
                return new ValidationResult { IsValid = false , Error = "Groups cannot be null." };
            }

            foreach (var group in _config.Groups)
            {
                if (group.StudentIds == null || group.StudentIds.Count < 2)
                {
                    return new ValidationResult { IsValid = false , Error = $"Group {group.GroupId} must have at least 2 students." };
                }
            }

            return new ValidationResult { IsValid = true };
        }
    }

    /// <summary>
    /// 同桌组策略的配置，定义同桌组和分配偏好。
    /// </summary>
    public class DeskMateConfiguration
    {
        /// <summary>同桌组列表。</summary>
        public List<DeskMateGroup> Groups { get; set; } = [];

        /// <summary>是否优先水平相邻分配（同行相邻列）。</summary>
        public bool PreferHorizontal { get; set; } = true;

        /// <summary>是否允许垂直相邻分配（同列相邻行）。</summary>
        public bool AllowVertical { get; set; } = false;
    }

    /// <summary>
    /// 同桌组，包含一组需要坐在一起的学生。
    /// </summary>
    public class DeskMateGroup
    {
        /// <summary>组唯一标识符。</summary>
        public string GroupId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>组内学生 ID 列表。</summary>
        public List<string> StudentIds { get; set; } = [];
    }
}
