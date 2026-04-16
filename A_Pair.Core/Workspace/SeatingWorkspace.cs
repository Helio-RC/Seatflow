using System;
using System.Collections.Generic;
using System.Linq;
using A_Pair.Core.Models;

namespace A_Pair.Core.Workspace
{
    public class SeatingWorkspace
    {
        private readonly List<Student> _students = new();
        private readonly List<Seat> _seats = new();

        public IReadOnlyList<Student> Students => _students;
        public SeatingContext Context { get; init; } = new();

        public SeatingWorkspace(IEnumerable<Student> students, IEnumerable<Seat> seats)
        {
            _students.AddRange(students ?? Enumerable.Empty<Student>());
            _seats.AddRange(seats ?? Enumerable.Empty<Seat>());
        }

        public bool TryAssignSeat(string seatId, string studentId, out string error)
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

        public IEnumerable<Seat> GetEmptySeats() => _seats.Where(s => s.IsAvailable && !s.IsFixed);

        public IEnumerable<Seat> FindSeats(Func<Seat, bool> predicate) => _seats.Where(predicate);

        public SeatingPlan BuildSeatingPlan()
        {
            var plan = new SeatingPlan();
            foreach (var seat in _seats)
            {
                if (!string.IsNullOrEmpty(seat.OccupantId))
                    plan.Assignments[seat.Id] = seat.OccupantId!;
            }
            return plan;
        }
    }

    public class SeatingContext { }

    public class SeatingPlan
    {
        public Dictionary<string, string> Assignments { get; set; } = new();
    }
}
