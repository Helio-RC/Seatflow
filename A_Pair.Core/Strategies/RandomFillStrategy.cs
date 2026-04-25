using A_Pair.Core.Workspace;

namespace A_Pair.Core.Strategies
{
    public class RandomFillStrategy (Random random) : ISeatingStrategy
    {
        private readonly Random _random = random ?? throw new ArgumentNullException(nameof(random));

        public RandomFillStrategy () : this(new Random()) { }

        public string Id { get; } = "RandomFill";
        public string Name { get; } = "RandomFill";
        public int Priority { get; set; } = 10;
        public bool IsEnabled { get; set; } = true;

        public Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
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

            int assignCount = Math.Min(emptySeats.Count , students.Count);
            for (int i = 0; i < assignCount && !cancellationToken.IsCancellationRequested; i++)
            {
                var seat = emptySeats[i];
                var student = students[i];
                workspace.TryAssignSeat(seat.Id , student.Id , out _);
            }

            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        public ValidationResult ValidateConfiguration () => new() { IsValid = true };
    }
}
