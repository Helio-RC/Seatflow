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
            Id = "RandomFill";
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
            ArgumentNullException.ThrowIfNull(workspace);

            var emptySeats = workspace.GetEmptySeats().ToList();
            var students = workspace.Students.Where(s => !workspace.BuildSeatingPlan().Assignments.ContainsValue(s.Id)).ToList();

            // Shuffle students
            for (int i = students.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (students[j] , students[i]) = (students[i] , students[j]);
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
