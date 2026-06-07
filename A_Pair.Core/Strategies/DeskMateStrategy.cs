using A_Pair.Core.Models;
using A_Pair.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Core.Strategies
{
    /// <summary>
    /// 同桌组策略（Priority=30，第三执行，在剩余空座中拼连续块）。
    /// 在 FixedSeat 和 FrontRowRotation 之后执行，从剩余空座中寻找连续座位块分配给同桌组。
    /// 优先水平相邻 → 回退纵向邻接 → 降级 BFS 连通分量。
    /// ⚠️ 此策略极不稳定：受前排分配和固定座位影响较大，组员被拆散后仅能就近安插单人。
    /// 支持从配置和 <see cref="AttributeBag"/> 扩展属性中读取同桌组定义。
    /// </summary>
    public class DeskMateStrategy : ISeatingStrategy
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

        /// <summary>策略 ID："DeskMate"。</summary>
        public string Id { get; } = "DeskMate";

        /// <summary>策略名称："DeskMate"。</summary>
        public string Name { get; } = "DeskMate";

        /// <summary>执行优先级：30（第三执行，在剩余空座中拼连续块）。</summary>
        public int Priority { get; set; } = 30;

        /// <summary>是否启用。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 执行同桌组分配：
        /// 1. 合并配置中的组和 <see cref="AttributeBag"/> 扩展属性中的组。
        /// 2. 按组大小降序处理，优先分配大组。
        /// 3. 尝试水平相邻 → 垂直相邻 → 任意连通分量三种策略。
        /// </summary>
        public Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            _logger.LogInformation("DeskMate 策略开始执行：学生 {StudentCount} 人，座位 {SeatCount} 个" ,
                workspace.Students.Count , workspace.GetEmptySeats().Count());

            // 获取所有未分配的学生ID
            var assignedStudentIds = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();
            var unassignedStudentIds = workspace.Students.Select(s => s.Id).Where(id => !assignedStudentIds.Contains(id)).ToHashSet();

            // 合并配置中的组和 Extensions 中的组（向后兼容）。
            // 注意：Extensions 合并的组会获得新的随机 GroupId，
            // 因此 GetPreAssignedMembers 无法在 _config.Groups 中找到它们。
            // 这意味着 near-occupied 路径不适用于 Extensions 组——这是预期行为，
            // 因为 Extensions 组在执行时才创建，不可能有"已被前序策略分配"的成员。
            var groups = new List<DeskMateGroup>(_config.Groups);
            foreach (var student in workspace.Students)
            {
                if (student.Extensions.TryGet<List<string>>("DeskMates" , out var mates) && mates != null)
                {
                    var existingGroup = groups.FirstOrDefault(g => g.StudentIds.Contains(student.Id));
                    if (existingGroup == null)
                    {
                        existingGroup = new DeskMateGroup();
                        groups.Add(existingGroup);
                    }
                    foreach (var mate in mates)
                    {
                        if (!existingGroup.StudentIds.Contains(mate))
                            existingGroup.StudentIds.Add(mate);
                    }
                    if (!existingGroup.StudentIds.Contains(student.Id))
                        existingGroup.StudentIds.Add(student.Id);
                }
            }

            // 去重并过滤掉已分配的学生
            var validGroups = new List<DeskMateGroup>();
            foreach (var g in groups)
            {
                var filtered = g.StudentIds.Distinct().Where(id => unassignedStudentIds.Contains(id)).ToList();
                if (filtered.Count < g.StudentIds.Count)
                {
                    // 部分成员已被之前的策略分配走，记录警告
                    var lostMembers = g.StudentIds.Except(filtered).ToList();
                    workspace.LogWarning(Id , DisplayNameConst , "DeskMate_Split" ,
                        string.Join(", " , g.StudentIds) , string.Join(", " , lostMembers));
                }
                if (filtered.Count >= 1)
                {
                    validGroups.Add(new DeskMateGroup { GroupId = g.GroupId , StudentIds = filtered });
                }
            }
            validGroups = validGroups.OrderByDescending(g => g.StudentIds.Count).ToList();

            if (validGroups.Count == 0)
            {
                _logger.LogDebug("DeskMate：无有效同桌组（每组至少 1 名未分配学生）");
                if (groups.Count > 0)
                    workspace.LogWarning(Id , DisplayNameConst , "DeskMate_AllSplit");
                return Task.FromResult(new StrategyExecutionResult { Success = true });
            }

            // 获取所有空座位
            var emptySeats = workspace.GetEmptySeats().ToList();
            if (emptySeats.Count == 0)
                return Task.FromResult(new StrategyExecutionResult { Success = true });

            var gridSeats = emptySeats.OfType<GridSeat>().ToList();

            foreach (var group in validGroups)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (group.StudentIds.Count == 0) continue;

                bool assigned = false;

                // 策略0：若原始组有成员已被其他策略分配，尝试将未分配成员放到其邻座
                var assignedMembers = GetPreAssignedMembers(group.GroupId , group.StudentIds.Count , unassignedStudentIds);
                if (assignedMembers.Count > 0)
                {
                    var occupiedSeats = workspace.FindSeats(s =>
                        s.OccupantId is not null && assignedMembers.Contains(s.OccupantId)).ToList();
                    if (occupiedSeats.Count > 0)
                    {
                        assigned = TryAssignNearOccupied(
                            workspace , group.StudentIds , occupiedSeats , emptySeats);
                    }
                }

                // 单人组仅尝试 near-occupied，不降级到网格分配策略
                if (group.StudentIds.Count >= 2)
                {
                    // 策略1：水平相邻分配（同行相邻列）
                    if (!assigned && gridSeats.Count > 0 && _config.PreferHorizontal)
                    {
                        assigned = TryAssignGroupToAdjacentGridSeats(workspace , group.StudentIds , gridSeats , horizontal: true);
                    }

                    // 策略2：垂直相邻分配（同列相邻行）
                    if (!assigned && _config.AllowVertical && gridSeats.Count > 0)
                    {
                        assigned = TryAssignGroupToAdjacentGridSeats(workspace , group.StudentIds , gridSeats , horizontal: false);
                    }

                    // 策略3：降级，尝试分配到任意相邻空座位（BFS 连通分量）
                    if (!assigned)
                    {
                        assigned = TryAssignGroupToAnyAdjacentSeats(workspace , group.StudentIds , emptySeats);
                    }
                }

                // 如果组分配成功，刷新空座位列表和未分配学生集合
                if (assigned)
                {
                    emptySeats = workspace.GetEmptySeats().ToList();
                    gridSeats = emptySeats.OfType<GridSeat>().ToList();
                    var currentAssignedIds = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();
                    unassignedStudentIds = workspace.Students.Select(s => s.Id)
                        .Where(id => !currentAssignedIds.Contains(id)).ToHashSet();
                }
                else
                {
                    // 所有策略都失败：记录警告（可能因座位不足或碎片化）
                    workspace.LogWarning(Id , DisplayNameConst , "DeskMate_NoSeats" ,
                        string.Join(", " , group.StudentIds));
                }
            }

            var remaining = workspace.GetEmptySeats().Count();
            _logger.LogInformation("DeskMate 策略完成：处理了 {GroupCount} 个同桌组，剩余 {Remaining} 个空座位" ,
                validGroups.Count , remaining);
            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        /// <summary>
        /// 获取原始配置组中已被其他策略分配走的成员 ID 列表。
        /// 若原始组不存在或人数未减少（无成员被其他策略先分配），返回空列表。
        /// </summary>
        /// <param name="groupId">组 ID。</param>
        /// <param name="currentCount">当前有效组中未分配学生数。</param>
        /// <param name="unassignedIds">全局未分配学生 ID 集合。</param>
        private List<string> GetPreAssignedMembers (string groupId , int currentCount , HashSet<string> unassignedIds)
        {
            var originalGroup = _config.Groups.FirstOrDefault(g => g.GroupId == groupId);
            if (originalGroup is null || originalGroup.StudentIds.Count <= currentCount)
                return [];

            return originalGroup.StudentIds
                .Where(id => !unassignedIds.Contains(id))
                .ToList();
        }

        /// <summary>
        /// 尝试将一组学生分配到网格布局中水平或垂直相邻的座位。
        /// </summary>
        /// <param name="workspace">工作区。</param>
        /// <param name="studentIds">学生 ID 列表。</param>
        /// <param name="gridSeats">可用的网格座位列表。</param>
        /// <param name="horizontal">true 表示水平相邻（同行），false 表示垂直相邻（同列）。</param>
        /// <returns>是否成功分配。</returns>
        private bool TryAssignGroupToAdjacentGridSeats (SeatingWorkspace workspace , List<string> studentIds , List<GridSeat> gridSeats , bool horizontal)
        {
            // 收集所有足够大的连续段，后续随机选一个（避免所有同桌组挤在左上角）
            var candidates = new List<List<GridSeat>>();

            var seatGroups = horizontal
                ? gridSeats.GroupBy(s => s.Row).OrderBy(g => g.Key)
                : gridSeats.GroupBy(s => s.Column).OrderBy(g => g.Key);

            foreach (var group in seatGroups)
            {
                var sorted = horizontal
                    ? group.OrderBy(s => s.Column).ToList()
                    : group.OrderBy(s => s.Row).ToList();

                var currentSegment = new List<GridSeat>();
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (currentSegment.Count == 0)
                    {
                        currentSegment.Add(sorted[i]);
                    }
                    else
                    {
                        int diff = horizontal
                            ? sorted[i].Column - currentSegment[^1].Column
                            : sorted[i].Row - currentSegment[^1].Row;

                        if (diff == 1)
                        {
                            currentSegment.Add(sorted[i]);
                        }
                        else
                        {
                            if (currentSegment.Count >= studentIds.Count)
                                candidates.Add(currentSegment);
                            currentSegment = [sorted[i]];
                        }
                    }
                }
                if (currentSegment.Count >= studentIds.Count)
                    candidates.Add(currentSegment);
            }

            if (candidates.Count > 0)
            {
                var segment = candidates[_random.Next(candidates.Count)];
                AssignSegment(workspace , segment , studentIds);
                return true;
            }
            return false;
        }

        private void AssignSegment (SeatingWorkspace workspace , List<GridSeat> segment , List<string> studentIds)
        {
            for (int i = 0; i < studentIds.Count; i++)
            {
                workspace.TryAssignSeat(segment[i].Id , studentIds[i] , out _);
            }
        }

        /// <summary>
        /// 尝试将未分配学生安排到已分配同桌成员的邻座中。
        /// 用于处理前排策略已将组内某人分配到前排后，其余成员找不到连续块的场景。
        /// 优先水平相邻，其次垂直相邻，再其次任意相邻空座。
        /// </summary>
        private bool TryAssignNearOccupied (
            SeatingWorkspace workspace ,
            List<string> unassignedIds ,
            List<Seat> occupiedSeats ,
            List<Seat> allEmptySeats)
        {
            var remaining = new List<string>(unassignedIds);
            Shuffle(remaining , _random);
            var emptySet = new HashSet<Seat>(allEmptySeats);

            foreach (var occSeat in occupiedSeats)
            {
                if (remaining.Count == 0) break;
                // 查找该座位周围的所有空座
                var nearby = emptySet.Where(e => AreSeatsAdjacent(occSeat , e)).ToList();
                Shuffle(nearby , _random);
                for (int i = 0; i < Math.Min(nearby.Count , remaining.Count); i++)
                {
                    workspace.TryAssignSeat(nearby[i].Id , remaining[i] , out _);
                    emptySet.Remove(nearby[i]);
                }
                remaining = remaining.Skip(Math.Min(nearby.Count , remaining.Count)).ToList();
            }

            return remaining.Count == 0;
        }

        /// <summary>
        /// 尝试将一组学生分配到任意相邻的空座位（使用 BFS 寻找连通分量）。
        /// 如果找不到足够大的连通分量，降级为顺序分配以保证功能可用。
        /// </summary>
        private bool TryAssignGroupToAnyAdjacentSeats (SeatingWorkspace workspace , List<string> studentIds , List<Seat> emptySeats)
        {
            if (emptySeats.Count < studentIds.Count) return false;

            int n = emptySeats.Count;
            var adjacency = new List<int>[n];
            for (int i = 0; i < n; i++)
                adjacency[i] = new List<int>();

            // 构建邻接关系
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (AreSeatsAdjacent(emptySeats[i] , emptySeats[j]))
                    {
                        adjacency[i].Add(j);
                        adjacency[j].Add(i);
                    }
                }
            }

            // BFS 寻找包含至少 studentIds.Count 个座位的连通分量
            var visited = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (visited[i]) continue;

                var component = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(i);
                visited[i] = true;

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    component.Add(cur);
                    foreach (var neighbor in adjacency[cur])
                    {
                        if (!visited[neighbor])
                        {
                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                if (component.Count >= studentIds.Count)
                {
                    // 将该连通分量中的前 studentIds.Count 个座位分配给学生
                    for (int s = 0; s < studentIds.Count; s++)
                    {
                        workspace.TryAssignSeat(emptySeats[component[s]].Id , studentIds[s] , out _);
                    }
                    return true;
                }
            }

            // 降级：若找不到足够大的连通分量，仍按顺序分配（保证功能可用）
            for (int i = 0; i < studentIds.Count && i < emptySeats.Count; i++)
            {
                workspace.TryAssignSeat(emptySeats[i].Id , studentIds[i] , out _);
            }
            return true;
        }

        /// <summary>
        /// 判断两个座位是否相邻。
        /// 网格座位：同行左右相邻或同列上下相邻。
        /// 极坐标座位：同环角度差 ≤45°，或相邻环角度几乎相同。
        /// 自由点座位：欧几里得距离 ≤1.5。
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