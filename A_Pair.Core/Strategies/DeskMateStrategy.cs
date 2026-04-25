using A_Pair.Core.Models;
using A_Pair.Core.Workspace;

namespace A_Pair.Core.Strategies
{
    public class DeskMateStrategy (DeskMateConfiguration config) : ISeatingStrategy
    {
        private readonly DeskMateConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));

        public DeskMateStrategy () : this(new DeskMateConfiguration()) { }

        public string Id { get; } = "DeskMate";
        public string Name { get; } = "DeskMate";
        public int Priority { get; set; } = 50;
        public bool IsEnabled { get; set; } = true;

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

                if (gridSeats.Count > 0 && _config.PreferHorizontal)
                {
                    assigned = TryAssignGroupToAdjacentGridSeats(workspace , group.StudentIds , gridSeats , horizontal: true);
                }

                if (!assigned && _config.AllowVertical && gridSeats.Count != 0)
                {
                    assigned = TryAssignGroupToAdjacentGridSeats(workspace , group.StudentIds , gridSeats , horizontal: false);
                }

                if (!assigned)
                {
                    // 降级：尝试分配到任意相邻空座位（不区分方向）
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

        private bool TryAssignGroupToAnyAdjacentSeats (SeatingWorkspace workspace , List<string> studentIds , List<Seat> emptySeats)
        {
            // 简化实现：寻找任意一组相邻座位（基于几何距离）
            // 此处作为占位符，实际可根据坐标计算距离
            if (emptySeats.Count < studentIds.Count) return false;

            // 简单顺序分配（非最佳，但确保功能可用）
            for (int i = 0; i < studentIds.Count && i < emptySeats.Count; i++)
            {
                workspace.TryAssignSeat(emptySeats[i].Id , studentIds[i] , out _);
            }
            return true;
        }

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

    public class DeskMateConfiguration
    {
        public List<DeskMateGroup> Groups { get; set; } = [];
        public bool PreferHorizontal { get; set; } = true;
        public bool AllowVertical { get; set; } = false;
    }

    public class DeskMateGroup
    {
        public string GroupId { get; set; } = System.Guid.NewGuid().ToString();
        public List<string> StudentIds { get; set; } = [];
    }
}