using System;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Workspace;
using A_Pair.Core.Strategies;

namespace A_Pair.Application.Scripting.Lua
{
    public class LuaScriptStrategy : ISeatingStrategy
    {
        public LuaScriptStrategy(string script)
        {
            Script = script;
            Id = Guid.NewGuid().ToString();
            Name = "LuaScript";
            Priority = 50;
            IsEnabled = true;
        }

        public string Script { get; }
        public string Id { get; }
        public string Name { get; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }

        public Task<StrategyExecutionResult> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken)
        {
            // Placeholder: actual Lua integration not included here. For now, no-op.
            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        public ValidationResult ValidateConfiguration() => new() { IsValid = true };
    }
}
