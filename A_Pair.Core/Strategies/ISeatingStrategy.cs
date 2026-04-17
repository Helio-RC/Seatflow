using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Workspace;

namespace A_Pair.Core.Strategies
{
    public interface ISeatingStrategy
    {
        string Id { get; }
        string Name { get; }
        int Priority { get; set; }
        bool IsEnabled { get; set; }

        Task<StrategyExecutionResult> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken);
        ValidationResult ValidateConfiguration();
    }

    public class StrategyExecutionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; } = true;
        public string? Error { get; set; }
    }
}