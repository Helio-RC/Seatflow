using A_Pair.Core.Workspace;

namespace A_Pair.Contracts.Interfaces
{
    /// <summary>
    /// 插件策略接口，供外部插件实现自定义排座逻辑。
    /// 继承自 <see cref="A_Pair.Core.Strategies.ISeatingStrategy"/> 的核心契约，
    /// 但独立定义以降低插件对内部类型的耦合。
    /// 插件开发者实现此接口后，系统通过 <see cref="A_Pair.Application.Plugins.PluginStrategyAdapter"/> 适配到策略管道。
    /// </summary>
    public interface IPluginSeatingStrategy
    {
        /// <summary>策略唯一标识符。</summary>
        string Id { get; }

        /// <summary>策略名称（用于日志和 UI 展示）。</summary>
        string Name { get; }

        /// <summary>执行优先级，数值越小越先执行。</summary>
        int Priority { get; set; }

        /// <summary>策略是否启用。</summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// 执行插件策略逻辑。
        /// </summary>
        /// <param name="workspace">当前工作区，包含学生和座位数据。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>执行结果。</returns>
        Task<PluginStrategyResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken);
    }

    /// <summary>
    /// 插件策略执行结果。
    /// </summary>
    public class PluginStrategyResult
    {
        /// <summary>是否执行成功。</summary>
        public bool Success { get; set; }

        /// <summary>结果描述消息。</summary>
        public string Message { get; set; } = string.Empty;
    }
}