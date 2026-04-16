using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Workspace;

namespace A_Pair.Core.Strategies
{
    public class FrontRowRotationStrategy : ISeatingStrategy
    {
        public FrontRowRotationStrategy()
        {
            Id = "FrontRowRotation";
            Name = "FrontRowRotation";
            Priority = 30;
            IsEnabled = true;
        }

        public string Id { get; }
        public string Name { get; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }

        public Task<StrategyExecutionResult> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken)
        {
            // Simple heuristic: prefer students with NeedsFrontRow or higher FrontRowPreferenceScore
            var emptySeats = workspace.GetEmptySeats().ToList();
            if (!emptySeats.Any()) return Task.FromResult(new StrategyExecutionResult { Success = true });

            // Identify front row seats by minimal Row if seat is GridSeat
            var frontRowIndex = emptySeats.Where(s => s is A_Pair.Core.Models.GridSeat).Cast<A_Pair.Core.Models.GridSeat>().Select(s => s.Row).DefaultIfEmpty(int.MaxValue).Min();
            var frontSeats = emptySeats.Where(s => s is A_Pair.Core.Models.GridSeat gs && gs.Row == frontRowIndex).ToList();

            var availableStudents = workspace.Students.Where(s => !workspace.BuildSeatingPlan().Assignments.Values.Contains(s.Id)).ToList();

            var ordered = availableStudents.OrderByDescending(s => (s.NeedsFrontRow ? 1000 : 0) + s.FrontRowPreferenceScore).ToList();

            int assignCount = Math.Min(frontSeats.Count, ordered.Count);
            for (int i = 0; i < assignCount && !cancellationToken.IsCancellationRequested; i++)
            {
                var seat = frontSeats[i];
                var student = ordered[i];
                workspace.TryAssignSeat(seat.Id, student.Id, out _);
            }

            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        public ValidationResult ValidateConfiguration() => new() { IsValid = true };
    }
}
