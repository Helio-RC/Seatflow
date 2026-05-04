using A_Pair.Core.Models;
using A_Pair.Core.Workspace;

namespace A_Pair.Core.Strategies
{
    /// <summary>
    /// 同桌组策略（Priority=50），将指定的学生组分配到相邻座位。
    /// 支持从配置和 <see cref="AttributeBag"/> 扩展属性中读取同桌组定义。
    /// 优先尝试水平相邻分配，其次垂直相邻，最后使用 BFS 寻找任意连通分量。
    /// </summary>
    public class DeskMateStrategy (DeskMateConfiguration config) : ISeatingStrategy
    {
        private readonly DeskMateConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));

        /// <summary>使用默认配置创建实例。</summary>
        public DeskMateStrategy () : this(new DeskMateConfiguration()) { }

        /// <summary>策略 ID："DeskMate"。</summary>
        public string Id { get; } = "DeskMate";

        /// <summary>策略名称："DeskMate"。</summary>
        public string Name { get; } = "DeskMate";

        /// <summary>执行优先级：50。</summary>
        public int Priority { get; set; } = 50;

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

            // 获取所有未分配的学生ID
            var assignedStudentIds = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();
            var unassignedStudentIds = workspace.Students.Select(s => s.Id).Where(id => !assignedStudentIds.Contains(id)).ToHashSet();

            // 合并配置中的组和 Extensions 中的组（向后兼容）
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
            var validGroups = groups
                .Select(g => new DeskMateGroup
                {
                    GroupId = g.GroupId ,
                    StudentIds = g.StudentIds.Distinct().Where(id => unassignedStudentIds.Contains(id)).ToList()
                })
                .Where(g => g.StudentIds.Count >= 2)
                .OrderByDescending(g => g.StudentIds.Count)
                .ToList();

            if (validGroups.Count == 0)
                return Task.FromResult(new StrategyExecutionResult { Success = true });

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

                // 策略1：水平相邻分配（同行相邻列）
                if (gridSeats.Count > 0 && _config.PreferHorizontal)
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

                // 如果组分配成功，刷新空座位列表
                if (assigned)
                {
                    emptySeats = workspace.GetEmptySeats().ToList();
                    gridSeats = emptySeats.OfType<GridSeat>().ToList();
                }
            }

            return Task.FromResult(new StrategyExecutionResult { Success = true });
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
            // 根据方向选择分组依据：水平时按行分组，垂直时按列分组
            var seatGroups = horizontal
                ? gridSeats.GroupBy(s => s.Row).OrderBy(g => g.Key)
                : gridSeats.GroupBy(s => s.Column).OrderBy(g => g.Key);

            foreach (var group in seatGroups)
            {
                // 在组内按次要坐标排序
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
                        // 检查次要坐标是否连续（差1）
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
                            {
                                AssignSegment(workspace , currentSegment , studentIds);
                                return true;
                            }
                            currentSegment.Clear();
                            currentSegment.Add(sorted[i]);
                        }
                    }
                }
                // 检查最后一个段
                if (currentSegment.Count >= studentIds.Count)
                {
                    AssignSegment(workspace , currentSegment , studentIds);
                    return true;
                }
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
                    double angleDiff = Math.Abs(pa.AngleDegrees - pb.AngleDegrees);
                    if (angleDiff <= 45.0) return true;
                }
                else
                {
                    if (Math.Abs(pa.AngleDegrees - pb.AngleDegrees) < angleTolerance)
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
                double distance = Math.Sqrt(dx * dx + dy * dy);
                return distance <= 1.5;
            }

            return false;
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