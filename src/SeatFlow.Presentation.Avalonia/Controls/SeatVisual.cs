namespace SeatFlow.Presentation.Avalonia.Controls;

/// <summary>
/// 不可变座位视觉快照 —— 画布渲染的输入数据（与 Core 座位模型解耦，便于测试与自绘）。
/// 坐标相对板面左上角。
/// </summary>
/// <param name="Id">座位唯一 ID（与快照/命中测试对应）。</param>
/// <param name="X">相对板面的 X 坐标。</param>
/// <param name="Y">相对板面的 Y 坐标。</param>
/// <param name="Width">座位宽。</param>
/// <param name="Height">座位高。</param>
/// <param name="IsOccupied">是否已分配学生。</param>
/// <param name="IsFixed">是否固定座位（FixedSeat 策略锁定）。</param>
/// <param name="IsDisabled">是否禁用座位（会场配置的不可用座位）。</param>
/// <param name="Label">座位显示文本（学生姓名；空座为 null）。</param>
public sealed record SeatVisual(
    string Id,
    double X,
    double Y,
    double Width,
    double Height,
    bool IsOccupied = false,
    bool IsFixed = false,
    bool IsDisabled = false,
    string? Label = null);

/// <summary>
/// 画布布局快照：座位集合 + 板面尺寸 + 版本号。
/// 数据更新约定（06 计划 §4）：整体替换引用并提升 <see cref="Version"/>，
/// 画布据此单次重绘，不做逐座位控件绑定。
/// </summary>
public sealed record SeatLayoutSnapshot(
    System.Collections.Generic.IReadOnlyList<SeatVisual> Seats,
    double BoardWidth,
    double BoardHeight,
    int Version = 0)
{
    /// <summary>空布局（未生成座位）。</summary>
    public static readonly SeatLayoutSnapshot Empty = new([], 0, 0);
}
