using SeatFlow.Application.Scripting.CSharp;
using SeatFlow.Application.Scripting.Lua;
using SeatFlow.Contracts.Interfaces;
using SeatFlow.Contracts.Models;
using SeatFlow.Core.Workspace;

namespace SeatFlow.Application.Plugins
{
    /// <summary>
    /// Lua 脚本插件适配器，将 <see cref="LuaScriptStrategy"/> 包装为 <see cref="IPluginSeatingStrategy"/>，
    /// 使 Lua 脚本能够作为插件策略被 <see cref="PluginManager"/> 加载。
    /// </summary>
    public class LuaScriptPluginAdapter : IPluginSeatingStrategy
    {
        private readonly LuaScriptStrategy _innerStrategy;

        /// <summary>
        /// 初始化 Lua 脚本插件适配器。
        /// </summary>
        /// <param name="scriptCode">Lua 脚本源代码。</param>
        /// <param name="name">策略显示名称。</param>
        /// <param name="priority">策略执行优先级。</param>
        public LuaScriptPluginAdapter (string scriptCode , string name , int priority)
        {
            var config = new LuaScriptConfiguration
            {
                StrategyName = name ,
                Priority = priority ,
                Enabled = true
            };
            _innerStrategy = new LuaScriptStrategy(scriptCode , config);
        }

        /// <inheritdoc />
        public string Id => _innerStrategy.Id;

        /// <inheritdoc />
        public string Name => _innerStrategy.Name;

        /// <inheritdoc />
        public int Priority { get => _innerStrategy.Priority; set => _innerStrategy.Priority = value; }

        /// <inheritdoc />
        public bool IsEnabled { get => _innerStrategy.IsEnabled; set => _innerStrategy.IsEnabled = value; }

        /// <inheritdoc />
        public async Task<PluginStrategyResult> ExecuteAsync (IPluginWorkspace workspace , CancellationToken cancellationToken)
        {
            var result = await _innerStrategy.ExecuteAsync((SeatingWorkspace)workspace , cancellationToken);
            return new PluginStrategyResult { Success = result.Success , Message = result.Message };
        }
    }

    /// <summary>
    /// C# 脚本插件适配器，将 <see cref="CSharpScriptStrategy"/> 包装为 <see cref="IPluginSeatingStrategy"/>，
    /// 使 C# 脚本能够作为插件策略被 <see cref="PluginManager"/> 加载。
    /// </summary>
    public class CSharpScriptPluginAdapter : IPluginSeatingStrategy
    {
        private readonly CSharpScriptStrategy _innerStrategy;

        /// <summary>
        /// 初始化 C# 脚本插件适配器。
        /// </summary>
        /// <param name="scriptCode">C# 脚本源代码。</param>
        /// <param name="name">策略显示名称。</param>
        /// <param name="priority">策略执行优先级。</param>
        public CSharpScriptPluginAdapter (string scriptCode , string name , int priority)
        {
            var config = new CSharpScriptConfiguration
            {
                StrategyName = name ,
                Priority = priority ,
                Enabled = true
            };
            _innerStrategy = new CSharpScriptStrategy(scriptCode , config);
        }

        /// <inheritdoc />
        public string Id => _innerStrategy.Id;

        /// <inheritdoc />
        public string Name => _innerStrategy.Name;

        /// <inheritdoc />
        public int Priority { get => _innerStrategy.Priority; set => _innerStrategy.Priority = value; }

        /// <inheritdoc />
        public bool IsEnabled { get => _innerStrategy.IsEnabled; set => _innerStrategy.IsEnabled = value; }

        /// <inheritdoc />
        public async Task<PluginStrategyResult> ExecuteAsync (IPluginWorkspace workspace , CancellationToken cancellationToken)
        {
            var result = await _innerStrategy.ExecuteAsync((SeatingWorkspace)workspace , cancellationToken);
            return new PluginStrategyResult { Success = result.Success , Message = result.Message };
        }
    }
}
