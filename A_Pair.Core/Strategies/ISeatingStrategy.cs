using A_Pair.Core.Workspace;

namespace A_Pair.Core.Strategies
{
    /// <summary>
    /// 座位安排策略接口，定义所有排座策略的通用契约。
    /// 策略按 <see cref="Priority"/> 升序依次执行，后执行的策略可覆盖先前分配。
    /// </summary>
    public interface ISeatingStrategy
    {
        /// <summary>策略唯一标识符。</summary>
        string Id { get; }

        /// <summary>策略名称（用于日志和 UI 展示）。</summary>
        string Name { get; }

        /// <summary>
        /// 执行优先级，数值越小越先执行。
        /// 内置策略优先级参考：RandomFill=10, FrontRowRotation=30, DeskMate=50, FixedSeat=100。
        /// </summary>
        int Priority { get; set; }

        /// <summary>策略是否启用。</summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// 执行策略逻辑，操作 <see cref="SeatingWorkspace"/> 进行座位分配。
        /// </summary>
        /// <param name="workspace">当前工作区，包含学生和座位数据。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>执行结果，包含成功状态和消息。</returns>
        Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken);

        /// <summary>
        /// 验证策略配置是否有效。
        /// </summary>
        /// <returns>验证结果。</returns>
        ValidationResult ValidateConfiguration ();
    }

    /// <summary>
    /// 策略执行结果，包含成功状态和描述消息。
    /// </summary>
    public record StrategyExecutionResult
    {
        /// <summary>是否执行成功。</summary>
        public bool Success { get; init; }

        /// <summary>执行结果描述消息。</summary>
        public string Message { get; init; } = string.Empty;
    }

    /// <summary>
    /// 配置验证结果。
    /// </summary>
    public record ValidationResult
    {
        /// <summary>配置是否有效。</summary>
        public bool IsValid { get; init; } = true;

        /// <summary>验证失败时的错误描述。</summary>
        public string? Error { get; init; }
    }
}