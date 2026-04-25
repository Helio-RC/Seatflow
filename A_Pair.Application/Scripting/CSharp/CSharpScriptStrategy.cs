using System.Reflection;
using A_Pair.Core.Strategies;
using A_Pair.Core.Workspace;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace A_Pair.Application.Scripting.CSharp
{
    public class CSharpScriptStrategy : ISeatingStrategy
    {
        private readonly string _code;
        private readonly CSharpScriptConfiguration _config;

        public CSharpScriptStrategy (string code , CSharpScriptConfiguration? config = null)
        {
            _code = code ?? throw new ArgumentNullException(nameof(code));
            _config = config ?? new CSharpScriptConfiguration();
            Id = Guid.NewGuid().ToString();
            Name = _config.StrategyName ?? "CSharpScript";
            Priority = _config.Priority;
            IsEnabled = _config.Enabled;
        }

        public string Id { get; }
        public string Name { get; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }

        public async Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_config.TimeoutMilliseconds);

            try
            {
                // 创建受限脚本选项
                var options = ScriptOptions.Default
                    .WithReferences(GetAllowedReferences())
                    .WithImports(GetAllowedImports());

                var globals = new ScriptGlobals { Workspace = workspace };
                var script = CSharpScript.Create(_code , options , typeof(ScriptGlobals));

                // 执行脚本，支持超时
                var task = script.RunAsync(globals , cancellationToken: cts.Token);
                await task.WaitAsync(cts.Token);

                return new StrategyExecutionResult { Success = true };
            }
            catch (OperationCanceledException)
            {
                return new StrategyExecutionResult { Success = false , Message = "脚本执行超时" };
            }
            catch (CompilationErrorException ex)
            {
                return new StrategyExecutionResult { Success = false , Message = $"编译错误: {string.Join("\n" , ex.Diagnostics)}" };
            }
            catch (Exception ex)
            {
                return new StrategyExecutionResult { Success = false , Message = $"执行失败: {ex.Message}" };
            }
        }

        public ValidationResult ValidateConfiguration ()
        {
            if (string.IsNullOrWhiteSpace(_code))
                return new ValidationResult { IsValid = false , Error = "脚本代码不能为空" };
            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// 获取允许引用的程序集白名单
        /// </summary>
        private Assembly[] GetAllowedReferences ()
        {
            // 仅允许必要的程序集
            var allowed = new List<Assembly>
            {
                typeof(object).Assembly,                 // System.Private.CoreLib
                typeof(Enumerable).Assembly,             // System.Linq
                typeof(SeatingWorkspace).Assembly,       // A_Pair.Core
                typeof(ScriptGlobals).Assembly           // A_Pair.Application
            };

            // 可根据配置添加额外引用
            return allowed.ToArray();
        }

        /// <summary>
        /// 获取允许导入的命名空间白名单
        /// </summary>
        private string[] GetAllowedImports () =>
            [
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "A_Pair.Core.Workspace",
                "A_Pair.Core.Models"
            ];
    }

    public class ScriptGlobals
    {
        public SeatingWorkspace? Workspace { get; set; }
    }

    public class CSharpScriptConfiguration
    {
        public string? StrategyName { get; set; }
        public int Priority { get; set; } = 60;
        public bool Enabled { get; set; } = true;
        public int TimeoutMilliseconds { get; set; } = 5000;
    }
}