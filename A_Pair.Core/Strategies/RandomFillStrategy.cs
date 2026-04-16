using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Workspace;

namespace A_Pair.Core.Strategies
{
    public class RandomFillStrategy : ISeatingStrategy
    {
        private readonly Random _random;

        public RandomFillStrategy() : this(new Random()) { }
        public RandomFillStrategy(Random random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
            Id = Guid.NewGuid().ToString();
            Name = "RandomFill";
            Priority = 10;
            IsEnabled = true;
        }

        public string Id { get; }
        public string Name { get; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }

        public Task<StrategyExecutionResult> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken)
        {
            if (workspace is null) throw new ArgumentNullException(nameof(workspace));

            var emptySeats = workspace.GetEmptySeats().ToList();
            var students = workspace.Students.Where(s => !workspace.BuildSeatingPlan().Assignments.Values.Contains(s.Id)).ToList();

            // Shuffle students
            for (int i = students.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                var tmp = students[i]; students[i] = students[j]; students[j] = tmp;
            }

            int assignCount = Math.Min(emptySeats.Count, students.Count);
            for (int i = 0; i < assignCount && !cancellationToken.IsCancellationRequested; i++)
            {
                var seat = emptySeats[i];
                var student = students[i];
                workspace.TryAssignSeat(seat.Id, student.Id, out _);
            }

            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        public ValidationResult ValidateConfiguration() => new() { IsValid = true };
    }
}
