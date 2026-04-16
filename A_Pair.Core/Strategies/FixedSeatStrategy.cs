using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Workspace;

namespace A_Pair.Core.Strategies
{
    public class FixedSeatStrategy : ISeatingStrategy
    {
        public FixedSeatStrategy()
        {
            Id = "FixedSeat";
            Name = "FixedSeat";
            Priority = 100;
            IsEnabled = true;
        }

        public string Id { get; }
        public string Name { get; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }

        public Task<StrategyExecutionResult> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken)
        {
            // Ensure seats marked IsFixed have their occupant preserved; if occupant missing, leave empty.
            foreach (var seat in workspace.FindSeats(s => s.IsFixed))
            {
                if (!string.IsNullOrEmpty(seat.OccupantId))
                {
                    // enforce availability
                    seat.IsAvailable = false;
                }
            }

            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        public ValidationResult ValidateConfiguration() => new() { IsValid = true };
    }
}
