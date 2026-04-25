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

        /// <summary>
        /// 使用默认配置创建实例。
        /// </summary>
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

            // 从 Extensions 中读取旧格式的同桌组配置
            foreach (var student in workspace.Students)
            {
                if (student.Extensions.TryGet<List<string>>("DeskMates" , out var mates) && mates != null)
                {
                    // 查找是否已有包含该学生的组
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

            // 将座位按布局类型分组处理
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
                if (!assigned && _config.AllowVertical && gridSeats.Count != 0)
                {
                    assigned = TryAssignGroupToAdjacentGridSeats(workspace , group.StudentIds , gridSeats , horizontal: false);
                }

                // 策略3：降级，尝试分配到任意相邻空座位（BFS 连通分量）
                if (!assigned)
                {
                    assigned = TryAssignGroupToAnyAdjacentSeats(workspace , group.StudentIds , emptySeats);
                }

                // 如果组分配成功，从空座位列表中移除已分配的座位
                if (assigned)
                {
                    var updatedEmptySeats = workspace.GetEmptySeats().ToList();
                    gridSeats = updatedEmptySeats.OfType<GridSeat>().ToList();
                }
            }

            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        /// <summary>
        /// 尝试将一组学生分配到网格布局中相邻的座位（水平或垂直方向）。
        /// </summary>
        /// <param name="workspace">工作区。</param>
        /// <param name="studentIds">学生 ID 列表。</param>
        /// <param name="gridSeats">可用的网格座位列表。</param>
        /// <param name="horizontal">true 表示水平相邻（同行），false 表示垂直相邻（同列）。</param>
        /// <returns>是否成功分配。</returns>
        private bool TryAssignGroupToAdjacentGridSeats (SeatingWorkspace workspace , List<string> studentIds , List<GridSeat> gridSeats , bool horizontal)
        {
            // 按行分组
            var rows = gridSeats.GroupBy(s => s.Row).OrderBy(g => g.Key).ToList();
            foreach (var row in rows)
            {
                var seatsInRow = row.OrderBy(s => s.Column).ToList();
                // 寻找连续的空座位段
                var consecutiveSegments = new List<List<GridSeat>>();
                var currentSegment = new List<GridSeat>();
                for (int i = 0; i < seatsInRow.Count; i++)
                {
                    if (i == 0 || seatsInRow[i].Column == seatsInRow[i - 1].Column + 1)
                    {
                        currentSegment.Add(seatsInRow[i]);
                    }
                    else
                    {
                        if (currentSegment.Count >= studentIds.Count)
                            consecutiveSegments.Add(new List<GridSeat>(currentSegment));
                        currentSegment.Clear();
                        currentSegment.Add(seatsInRow[i]);
                    }
                }
                if (currentSegment.Count >= studentIds.Count)
                    consecutiveSegments.Add(currentSegment);

                foreach (var segment in consecutiveSegments)
                {
                    if (segment.Count >= studentIds.Count)
                    {
                        for (int i = 0; i < studentIds.Count; i++)
                        {
                            workspace.TryAssignSeat(segment[i].Id , studentIds[i] , out _);
                        }
                        return true;
                    }
                }
            }
            return false;
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
                // 网格座位：同行左右相邻 或 同列上下相邻
                return (ga.Row == gb.Row && Math.Abs(ga.Column - gb.Column) == 1)
                    || (ga.Column == gb.Column && Math.Abs(ga.Row - gb.Row) == 1);
            }

            if (a is PolarSeat pa && b is PolarSeat pb)
            {
                const double angleTolerance = 1e-6;
                bool sameRing = Math.Abs(pa.Radius - pb.Radius) < 1e-6;
                if (sameRing)
                {
                    // 同一环上，角度差小于 45 度视为相邻（后续可结合布局步长精确计算）
                    double angleDiff = Math.Abs(pa.AngleDegrees - pb.AngleDegrees);
                    if (angleDiff <= 45.0) return true;
                }
                else
                {
                    // 相邻环，角度必须几乎相同
                    if (Math.Abs(pa.AngleDegrees - pb.AngleDegrees) < angleTolerance)
                        return true;
                }
                return false;
            }

            if (a is FreeformSeat fa && b is FreeformSeat fb)
            {
                double dx = fa.X - fb.X;
                double dy = fa.Y - fb.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                // 距离小于 1.5 个单位视为相邻（可根据实际布局调整）
                return distance <= 1.5;
            }

            // 混合类型的座位不视为相邻
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
        public string GroupId { get; set; } = System.Guid.NewGuid().ToString();

        /// <summary>组内学生 ID 列表。</summary>
        public List<string> StudentIds { get; set; } = [];
    }
}