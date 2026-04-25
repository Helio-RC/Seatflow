using A_Pair.Application.Scripting.CSharp;
using A_Pair.Application.Scripting.Lua;
using A_Pair.Contracts.Interfaces;
using A_Pair.Core.Workspace;

namespace A_Pair.Application.Plugins
{
    /// <summary>
    /// Lua 脚本插件适配器，将 <see cref="LuaScriptStrategy"/> 包装为 <see cref="IPluginSeatingStrategy"/>，
    /// 使 Lua 脚本能够作为插件策略被 <see cref="PluginManager"/> 加载。
    /// </summary>
    /// <remarks>
    /// 从 <see cref="PluginManifest"/> 中提取策略名称、优先级和启用状态，
    /// 构造 <see cref="LuaScriptConfiguration"/> 并创建内部的 <see cref="LuaScriptStrategy"/> 实例。
    /// </remarks>
    public class LuaScriptPluginAdapter : IPluginSeatingStrategy
    {
        private readonly LuaScriptStrategy _innerStrategy;

        /// <summary>
        /// 初始化 Lua 脚本插件适配器。
        /// </summary>
        /// <param name="scriptCode">Lua 脚本源代码。</param>
        /// <param name="manifest">插件清单，用于提取策略元数据。</param>
        public LuaScriptPluginAdapter (string scriptCode , PluginManifest manifest)
        {
            var config = new LuaScriptConfiguration
            {
                StrategyName = manifest.Name ,
                Priority = manifest.Priority ,
                Enabled = manifest.Enabled
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
        public async Task<PluginStrategyResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            var result = await _innerStrategy.ExecuteAsync(workspace , cancellationToken);
            return new PluginStrategyResult { Success = result.Success , Message = result.Message };
        }
    }

    /// <summary>
    /// C# 脚本插件适配器，将 <see cref="CSharpScriptStrategy"/> 包装为 <see cref="IPluginSeatingStrategy"/>，
    /// 使 C# 脚本能够作为插件策略被 <see cref="PluginManager"/> 加载。
    /// </summary>
    /// <remarks>
    /// 从 <see cref="PluginManifest"/> 中提取策略名称、优先级和启用状态，
    /// 构造 <see cref="CSharpScriptConfiguration"/> 并创建内部的 <see cref="CSharpScriptStrategy"/> 实例。
    /// </remarks>
    public class CSharpScriptPluginAdapter : IPluginSeatingStrategy
    {
        private readonly CSharpScriptStrategy _innerStrategy;

        /// <summary>
        /// 初始化 C# 脚本插件适配器。
        /// </summary>
        /// <param name="scriptCode">C# 脚本源代码。</param>
        /// <param name="manifest">插件清单，用于提取策略元数据。</param>
        public CSharpScriptPluginAdapter (string scriptCode , PluginManifest manifest)
        {
            var config = new CSharpScriptConfiguration
            {
                StrategyName = manifest.Name ,
                Priority = manifest.Priority ,
                Enabled = manifest.Enabled
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
        public async Task<PluginStrategyResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            var result = await _innerStrategy.ExecuteAsync(workspace , cancellationToken);
            return new PluginStrategyResult { Success = result.Success , Message = result.Message };
        }
    }
}