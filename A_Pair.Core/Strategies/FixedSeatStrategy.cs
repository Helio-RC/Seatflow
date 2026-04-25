using A_Pair.Core.Workspace;

namespace A_Pair.Core.Strategies
{
    public class FixedSeatStrategy (FixedSeatConfiguration config) : ISeatingStrategy
    {
        private readonly FixedSeatConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));

        public FixedSeatStrategy () : this(new FixedSeatConfiguration()) { }

        public string Id { get; } = "FixedSeat";
        public string Name { get; } = "FixedSeat";
        public int Priority { get; set; } = 100;
        public bool IsEnabled { get; set; } = true;

        public Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            // 应用配置中的固定分配
            foreach (var kv in _config.FixedAssignments)
            {
                var seat = workspace.FindSeats(s => s.Id == kv.Key).FirstOrDefault();
                if (seat != null && !seat.IsFixed)
                {
                    seat.IsFixed = true;
                    if (!string.IsNullOrEmpty(kv.Value))
                    {
                        workspace.TryAssignSeat(seat.Id , kv.Value , out _);
                    }
                }
            }

            // 确保所有标记为 IsFixed 的座位保持不变
            foreach (var seat in workspace.FindSeats(s => s.IsFixed))
            {
                if (!string.IsNullOrEmpty(seat.OccupantId))
                {
                    seat.IsAvailable = false;
                }
            }

            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        public ValidationResult ValidateConfiguration ()
        {
            if (_config.FixedAssignments == null)
            {
                return new ValidationResult { IsValid = false , Error = "FixedAssignments cannot be null." };
            }
            return new ValidationResult { IsValid = true };
        }
    }

    public class FixedSeatConfiguration
    {
        public Dictionary<string , string> FixedAssignments { get; set; } = [];
    }
}