using A_Pair.Core.Workspace;

namespace A_Pair.Core.Strategies
{
    /// <summary>
    /// 座位安排策略接口，定义所有排座策略的通用契约。
    /// </summary>
    /// <remarks>
    /// <b>执行顺序与覆盖规则</b>
    /// <para>
    /// 策略按 <see cref="Priority"/> 升序执行（数值越小越先执行）。
    /// 管道采用 <b>"基线 → 优化 → 最终裁决"</b>模式：
    /// </para>
    /// <list type="bullet">
    /// <item>低 Priority（先执行）= 建立基线分配，为后续策略提供初始状态</item>
    /// <item>中 Priority（中期执行）= 对特定维度进行优化调整，可覆盖先前分配</item>
    /// <item>高 Priority（最后执行）= 最终裁决，强制覆盖以保证不可妥协的约束</item>
    /// </list>
    /// <para>
    /// 内置策略执行顺序：RandomFill(10) → FrontRowRotation(30) → DeskMate(50) → FixedSeat(100)。
    /// 后执行的策略可直接修改 <see cref="Workspace.SeatingWorkspace"/> 中的座位状态（包括清空 OccupantId 后重新分配），
    /// 因此高优先级策略具有对低优先级策略结果的<b>覆盖权</b>。
    /// </para>
    /// </remarks>
    public interface ISeatingStrategy
    {
        /// <summary>策略唯一标识符。</summary>
        string Id { get; }

        /// <summary>策略名称（用于日志和 UI 展示）。</summary>
        string Name { get; }

        /// <summary>
        /// 执行优先级，数值越小越先执行（升序）。
        /// 注意：低 Priority 先执行建立基线，高 Priority 后执行具有覆盖权。
        /// 内置策略：RandomFill=10（基线填充）, FrontRowRotation=30（前排优化）,
        /// DeskMate=50（同桌优化）, FixedSeat=100（最终固定）。
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