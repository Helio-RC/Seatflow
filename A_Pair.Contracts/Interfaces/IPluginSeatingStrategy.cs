using A_Pair.Core.Workspace;

namespace A_Pair.Contracts.Interfaces
{
    public interface IPluginSeatingStrategy
    {
        string Id { get; }
        string Name { get; }
        int Priority { get; set; }
        bool IsEnabled { get; set; }

        Task<PluginStrategyResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken);
    }

    public class PluginStrategyResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}