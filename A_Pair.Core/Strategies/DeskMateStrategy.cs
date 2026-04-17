using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Workspace;
using System.Collections.Generic;

namespace A_Pair.Core.Strategies
{
    public class DeskMateConfiguration
    {
        public List<DeskMateGroup> Groups { get; set; } = new();
    }

    public class DeskMateGroup
    {
        public List<string> StudentIds { get; set; } = new();
    }
    public class DeskMateStrategy : ISeatingStrategy
    {
        private readonly DeskMateConfiguration _config;
        public DeskMateStrategy() : this(new DeskMateConfiguration()) { }
        public DeskMateStrategy(DeskMateConfiguration config)
        {
            _config = config;
            Id = "DeskMate";
            Name = "DeskMate";
            Priority = 50;
            IsEnabled = true;
        }

        public string Id { get; }
        public string Name { get; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }

        // Simple greedy algorithm: for configured pairs (via Extensions) try to seat them adjacent horizontally
        public Task<StrategyExecutionResult> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken)
        {
            var pairs = new List<(string a, string b)>();

            // Gather pairs from student Extensions if present
            foreach (var s in workspace.Students)
            {
                if (s.Extensions.TryGet<System.Collections.Generic.List<string>>("DeskMates", out var mates) && mates != null)
                {
                    foreach (var m in mates)
                    {
                        pairs.Add((s.Id, m));
                    }
                }
            }

            if (!pairs.Any()) return Task.FromResult(new StrategyExecutionResult { Success = true });

            // For grid seats, map seats by row/column
            var gridSeats = workspace.FindSeats(s => s is A_Pair.Core.Models.GridSeat).Cast<A_Pair.Core.Models.GridSeat>().ToList();

            foreach (var (a, b) in pairs)
            {
                if (cancellationToken.IsCancellationRequested) break;
                // if either already assigned, skip
                var plan = workspace.BuildSeatingPlan();
                if (plan.Assignments.Values.Contains(a) || plan.Assignments.Values.Contains(b)) continue;

                // find two adjacent empty seats in same row
                var grouped = gridSeats.GroupBy(s => s.Row);
                bool assigned = false;
                foreach (var g in grouped)
                {
                    var empties = g.Where(s => s.IsAvailable && !s.IsFixed).OrderBy(s => s.Column).ToList();
                    for (int i = 0; i < empties.Count - 1; i++)
                    {
                        if (empties[i].Column + 1 == empties[i + 1].Column)
                        {
                            workspace.TryAssignSeat(empties[i].Id, a, out _);
                            workspace.TryAssignSeat(empties[i + 1].Id, b, out _);
                            assigned = true;
                            break;
                        }
                    }
                    if (assigned) break;
                }
            }

            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        public ValidationResult ValidateConfiguration() => new() { IsValid = true };
    }
}
