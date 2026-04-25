using A_Pair.Core.Workspace;

namespace A_Pair.Application.Commands
{
    public class AssignSeatCommand (string seatId , string studentId) : IUndoableCommand
    {
        public string Id { get; } = System.Guid.NewGuid().ToString();
        public string SeatId { get; } = seatId;
        public string StudentId { get; } = studentId;

        public Task<bool> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default)
        {
            var ok = workspace.TryAssignSeat(SeatId , StudentId , out _);
            return Task.FromResult(ok);
        }

        public Task<bool> UndoAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default)
        {
            var seat = workspace.FindSeats(s => s.Id == SeatId).FirstOrDefault();
            if (seat == null) return Task.FromResult(false);
            if (seat.OccupantId == StudentId)
            {
                seat.OccupantId = null;
                seat.IsAvailable = true;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }
}
