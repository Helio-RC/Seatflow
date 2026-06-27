using System.Reflection;
using A_Pair.Core.Strategies;
using A_Pair.Core.Workspace;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Application.Scripting.CSharp
{
    /// <summary>
    /// C# 脚本策略，使用 Roslyn 脚本引擎在运行时编译并执行 C# 代码作为座位分配策略。
    /// </summary>
    public class CSharpScriptStrategy : ISeatingStrategy
    {
        private readonly string _code;
        private readonly CSharpScriptConfiguration _config;
        private readonly ILogger<CSharpScriptStrategy> _logger;

        public CSharpScriptStrategy (string code , CSharpScriptConfiguration? config = null , ILogger<CSharpScriptStrategy>? logger = null)
        {
            _code = code ?? throw new ArgumentNullException(nameof(code));
            _config = config ?? new CSharpScriptConfiguration();
            _logger = logger ?? NullLogger<CSharpScriptStrategy>.Instance;
            Id = Guid.NewGuid().ToString();
            Name = _config.StrategyName ?? "CSharpScript";
            Priority = _config.Priority;
            IsEnabled = _config.Enabled;
        }

        /// <inheritdoc />
        public string Id { get; }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public int Priority { get; set; }

        /// <inheritdoc />
        public bool IsEnabled { get; set; }

        /// <inheritdoc />
        public async Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_config.TimeoutMilliseconds);

            try
            {
                _logger.LogInformation("C# 脚本策略开始执行：{Name}" , Name);
                var options = ScriptOptions.Default
                    .WithReferences(GetAllowedReferences())
                    .WithImports(GetAllowedImports());

                var globals = new ScriptGlobals { Workspace = workspace };
                var script = CSharpScript.Create(_code , options , typeof(ScriptGlobals));

                var task = script.RunAsync(globals , cancellationToken: cts.Token);
                await task.WaitAsync(cts.Token);

                return new StrategyExecutionResult { Success = true };
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("C# 脚本执行超时：{Name}（{Timeout}ms）" , Name , _config.TimeoutMilliseconds);
                return new StrategyExecutionResult { Success = false , Message = "脚本执行超时" };
            }
            catch (CompilationErrorException ex)
            {
                _logger.LogWarning(ex , "C# 脚本编译错误：{Name}" , Name);
                return new StrategyExecutionResult { Success = false , Message = $"编译错误: {string.Join("\n" , ex.Diagnostics)}" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex , "C# 脚本执行失败：{Name}" , Name);
                return new StrategyExecutionResult { Success = false , Message = $"执行失败: {ex.Message}" };
            }
        }

        /// <inheritdoc />
        public ValidationResult ValidateConfiguration ()
        {
            if (string.IsNullOrWhiteSpace(_code))
                return new ValidationResult { IsValid = false , Error = "脚本代码不能为空" };
            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// 获取允许引用的程序集白名单，限制脚本可使用的 API 范围。
        /// </summary>
        /// <returns>允许引用的程序集数组。</returns>
        private static Assembly[] GetAllowedReferences ()
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
            return [.. allowed];
        }

        /// <summary>
        /// 获取允许导入的命名空间白名单。
        /// </summary>
        /// <returns>允许导入的命名空间数组。</returns>
        private static string[] GetAllowedImports () =>
            [
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "A_Pair.Core.Workspace",
                "A_Pair.Core.Models"
            ];
    }

    /// <summary>
    /// 传递给 C# 脚本的全局对象，脚本通过此对象访问 <see cref="SeatingWorkspace"/>。
    /// </summary>
    public class ScriptGlobals
    {
        /// <summary>
        /// 获取当前座位工作区实例。
        /// </summary>
        public SeatingWorkspace? Workspace { get; init; }
    }

    /// <summary>
    /// C# 脚本策略的配置选项。
    /// </summary>
    public class CSharpScriptConfiguration
    {
        /// <summary>
        /// 获取或设置策略显示名称。
        /// </summary>
        public string? StrategyName { get; set; }

        /// <summary>
        /// 获取或设置策略在管道中的执行优先级。
        /// </summary>
        public int Priority { get; set; } = 60;

        /// <summary>
        /// 获取或设置一个值，指示策略是否启用。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 获取或设置脚本执行超时时间（毫秒）。默认 5000 毫秒。
        /// </summary>
        public int TimeoutMilliseconds { get; set; } = 5000;
    }
}