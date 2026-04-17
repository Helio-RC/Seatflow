using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using A_Pair.Core.Workspace;
using A_Pair.Core.Strategies;

namespace A_Pair.Application.Scripting.CSharp
{
    public class CSharpScriptStrategy : ISeatingStrategy
    {
        private readonly string _code;

        public CSharpScriptStrategy(string code)
        {
            _code = code;
            Id = Guid.NewGuid().ToString();
            Name = "CSharpScript";
            Priority = 60;
            IsEnabled = true;
        }

        public string Id { get; }
        public string Name { get; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }

        public async Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            var options = ScriptOptions.Default.WithImports("System");
            try
            {
                var script = CSharpScript.Create(_code , options , typeof(ScriptGlobals));
                var globals = new ScriptGlobals { Workspace = workspace };
                var state = await script.RunAsync(globals , cancellationToken);
            }
            catch (Exception ex)
            {
                return new StrategyExecutionResult { Success = false , Message = ex.Message };
            }

            return new StrategyExecutionResult { Success = true };
        }

        public ValidationResult ValidateConfiguration() => new() { IsValid = true };
    }

    public class ScriptGlobals
    {
        public SeatingWorkspace? Workspace { get; set; }
    }
}
