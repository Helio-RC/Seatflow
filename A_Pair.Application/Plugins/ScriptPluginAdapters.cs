using A_Pair.Application.Scripting.CSharp;
using A_Pair.Application.Scripting.Lua;
using A_Pair.Contracts.Interfaces;
using A_Pair.Core.Workspace;

namespace A_Pair.Application.Plugins
{
    /// <summary>
    /// Lua 脚本插件适配器，将 LuaScriptStrategy 包装为 IPluginSeatingStrategy
    /// </summary>
    public class LuaScriptPluginAdapter : IPluginSeatingStrategy
    {
        private readonly LuaScriptStrategy _innerStrategy;

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

        public string Id => _innerStrategy.Id;
        public string Name => _innerStrategy.Name;
        public int Priority { get => _innerStrategy.Priority; set => _innerStrategy.Priority = value; }
        public bool IsEnabled { get => _innerStrategy.IsEnabled; set => _innerStrategy.IsEnabled = value; }

        public async Task<PluginStrategyResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            var result = await _innerStrategy.ExecuteAsync(workspace , cancellationToken);
            return new PluginStrategyResult { Success = result.Success , Message = result.Message };
        }
    }

    /// <summary>
    /// C# 脚本插件适配器
    /// </summary>
    public class CSharpScriptPluginAdapter : IPluginSeatingStrategy
    {
        private readonly CSharpScriptStrategy _innerStrategy;

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

        public string Id => _innerStrategy.Id;
        public string Name => _innerStrategy.Name;
        public int Priority { get => _innerStrategy.Priority; set => _innerStrategy.Priority = value; }
        public bool IsEnabled { get => _innerStrategy.IsEnabled; set => _innerStrategy.IsEnabled = value; }

        public async Task<PluginStrategyResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            var result = await _innerStrategy.ExecuteAsync(workspace , cancellationToken);
            return new PluginStrategyResult { Success = result.Success , Message = result.Message };
        }
    }
}