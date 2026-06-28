using SeatFlow.Contracts.Interfaces;
using SeatFlow.Core.Strategies;
using SeatFlow.Core.Workspace;

namespace SeatFlow.Application.Plugins
{
    /// <summary>
    /// 将 <see cref="IPluginSeatingStrategy"/> 适配为 <see cref="ISeatingStrategy"/>，
    /// 使插件策略能够加入内置的策略执行管道。
    /// </summary>
    /// <remarks>
    /// 适配器模式的应用：插件策略实现 <see cref="IPluginSeatingStrategy"/> 接口（定义在 Contracts 层），
    /// 而策略执行管道要求 <see cref="ISeatingStrategy"/> 接口（定义在 Core 层）。
    /// 此适配器桥接两者，代理所有属性调用和方法调用到内部的插件策略实例。
    /// </remarks>
    /// <param name="pluginStrategy">要适配的插件策略实例。</param>
    public class PluginStrategyAdapter (IPluginSeatingStrategy pluginStrategy) : ISeatingStrategy
    {
        private readonly IPluginSeatingStrategy _pluginStrategy = pluginStrategy;

        /// <inheritdoc />
        public string Id => _pluginStrategy.Id;

        /// <inheritdoc />
        public string Name => _pluginStrategy.Name;

        /// <inheritdoc />
        public int Priority
        {
            get => _pluginStrategy.Priority;
            set => _pluginStrategy.Priority = value;
        }

        /// <inheritdoc />
        public bool IsEnabled
        {
            get => _pluginStrategy.IsEnabled;
            set => _pluginStrategy.IsEnabled = value;
        }

        /// <inheritdoc />
        public async Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            var result = await _pluginStrategy.ExecuteAsync(workspace , cancellationToken);
            return new StrategyExecutionResult
            {
                Success = result.Success ,
                Message = result.Message
            };
        }

        /// <inheritdoc />
        public ValidationResult ValidateConfiguration ()
        {
            // 插件策略的配置验证由插件自身负责，此处默认返回有效
            return new ValidationResult { IsValid = true };
        }
    }
}