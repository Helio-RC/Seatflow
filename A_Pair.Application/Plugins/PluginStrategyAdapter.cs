using System.Threading;
using System.Threading.Tasks;
using A_Pair.Contracts.Interfaces;
using A_Pair.Core.Strategies;
using A_Pair.Core.Workspace;

namespace A_Pair.Application.Plugins
{
    /// <summary>
    /// 将 IPluginSeatingStrategy 适配为 ISeatingStrategy，以便加入策略执行管道
    /// </summary>
    public class PluginStrategyAdapter : ISeatingStrategy
    {
        private readonly IPluginSeatingStrategy _pluginStrategy;

        public PluginStrategyAdapter (IPluginSeatingStrategy pluginStrategy)
        {
            _pluginStrategy = pluginStrategy;
        }

        public string Id => _pluginStrategy.Id;
        public string Name => _pluginStrategy.Name;

        public int Priority
        {
            get => _pluginStrategy.Priority;
            set => _pluginStrategy.Priority = value;
        }

        public bool IsEnabled
        {
            get => _pluginStrategy.IsEnabled;
            set => _pluginStrategy.IsEnabled = value;
        }

        public async Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            var result = await _pluginStrategy.ExecuteAsync(workspace , cancellationToken);
            return new StrategyExecutionResult
            {
                Success = result.Success ,
                Message = result.Message
            };
        }

        public ValidationResult ValidateConfiguration ()
        {
            // 插件策略的配置验证由插件自身负责，此处默认返回有效
            return new ValidationResult { IsValid = true };
        }
    }
}