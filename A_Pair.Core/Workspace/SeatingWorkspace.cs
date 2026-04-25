using A_Pair.Core.Models;

namespace A_Pair.Core.Workspace
{
    public class SeatingWorkspace
    {
        private readonly List<Student> _students = [];
        private readonly List<Seat> _seats = [];

        public IReadOnlyList<Student> Students => _students;
        public class SeatingContext
        {
            /// <summary>
            /// 当前使用的教室布局定义
            /// </summary>
            public ClassroomLayoutDefinition? Layout { get; set; }

            /// <summary>
            /// 当前轮换周期（如“第3周”）
            /// </summary>
            public string? RotationCycle { get; set; }

            /// <summary>
            /// 座位安排生效日期
            /// </summary>
            public DateTime EffectiveDate { get; set; } = DateTime.UtcNow;

            /// <summary>
            /// 用于策略的共享元数据
            /// </summary>
            public Dictionary<string , object> Metadata { get; set; } = [];
        }

        public SeatingWorkspace (IEnumerable<Student> students , IEnumerable<Seat> seats)
        {
            _students.AddRange(students ?? Enumerable.Empty<Student>());
            _seats.AddRange(seats ?? Enumerable.Empty<Seat>());
        }

        public bool TryAssignSeat (string seatId , string studentId , out string error)
        {
            error = string.Empty;
            var seat = _seats.FirstOrDefault(s => s.Id == seatId);
            var student = _students.FirstOrDefault(s => s.Id == studentId);
            if (seat == null) { error = "Seat not found"; return false; }
            if (student == null) { error = "Student not found"; return false; }
            if (!seat.IsAvailable) { error = "Seat not available"; return false; }
            if (seat.IsFixed && seat.OccupantId != studentId) { error = "Seat is fixed by another student"; return false; }

            // Prevent assigning the same student to multiple seats
            var alreadyAssignedSeat = _seats.FirstOrDefault(s => s.OccupantId == studentId && s.Id != seatId);
            if (alreadyAssignedSeat != null)
            {
                error = "Student already assigned to another seat";
                return false;
            }

            seat.OccupantId = studentId;
            seat.IsAvailable = false;
            student.RecentSeatHistory.Add(seatId);
            return true;
        }

        public IEnumerable<Seat> GetEmptySeats () => _seats.Where(s => s.IsAvailable && !s.IsFixed);

        public IEnumerable<Seat> FindSeats (Func<Seat , bool> predicate) => _seats.Where(predicate);

        public SeatingPlan BuildSeatingPlan ()
        {
            var plan = new SeatingPlan();
            foreach (var seat in _seats)
            {
                if (!string.IsNullOrEmpty(seat.OccupantId))
                    plan.Assignments[seat.Id] = seat.OccupantId!;
            }
            return plan;
        }

        public void ApplySnapshotAssignments (Dictionary<string , string> seatAssignments)
        {
            // 清空所有当前分配
            foreach (var seat in _seats)
            {
                seat.OccupantId = null;
                seat.IsAvailable = true;
            }

            // 应用快照中的分配
            foreach (var kv in seatAssignments)
            {
                var seat = _seats.FirstOrDefault(s => s.Id == kv.Key);
                if (seat != null && _students.Any(st => st.Id == kv.Value))
                {
                    seat.OccupantId = kv.Value;
                    seat.IsAvailable = false;
                }
            }
        }
    }
    public class SeatingPlan
    {
        public Dictionary<string , string> Assignments { get; set; } = [];
    }
}
