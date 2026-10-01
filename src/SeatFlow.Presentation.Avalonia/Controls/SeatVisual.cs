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
/// <param name="Label">座位显示文本（已分配为学生姓名；空座为 null 时不绘制）。</param>
/// <param name="SeatLabel">座位编号文本（Grid R1C1 / Polar 环座 / Freeform 序号；供键盘/提示辅助）。</param>
/// <param name="StudentId">已分配学生 ID（拖拽契约使用）。</param>
/// <param name="IsSwapSource">是否为交换模式下的源座位（选中态）。</param>
/// <param name="IsDropTarget">是否为拖拽悬停目标。</param>
/// <param name="IsDataStale">数据已失效（快照回滚预览等场景高亮）。</param>
public sealed record SeatVisual(
    string Id,
    double X,
    double Y,
    double Width,
    double Height,
    bool IsOccupied = false,
    bool IsFixed = false,
    bool IsDisabled = false,
    string? Label = null,
    string? SeatLabel = null,
    string? StudentId = null,
    bool IsSwapSource = false,
    bool IsDropTarget = false,
    bool IsDataStale = false);

/// <summary>板面覆盖物（讲台 / 门 / 其他标记），坐标相对板面左上角。</summary>
/// <param name="X">相对板面的 X 坐标。</param>
/// <param name="Y">相对板面的 Y 坐标。</param>
/// <param name="Width">宽度。</param>
/// <param name="Height">高度。</param>
/// <param name="Label">显示文本（讲台 / 门 / 类型名）。</param>
/// <param name="IsRound">是否圆形（讲台在部分布局下用半圆/圆角）。</param>
/// <param name="IsDoor">是否门（虚线边框样式）。</param>
public sealed record BoardOverlay(
    double X,
    double Y,
    double Width,
    double Height,
    string? Label = null,
    bool IsRound = false,
    bool IsDoor = false);

/// <summary>
/// 画布布局快照：座位集合 + 覆盖物 + 板面尺寸 + 版本号。
/// 数据更新约定（06 计划 §4）：整体替换引用并提升 <see cref="Version"/>，
/// 画布据此单次重绘，不做逐座位控件绑定。
/// </summary>
public sealed record SeatLayoutSnapshot(
    System.Collections.Generic.IReadOnlyList<SeatVisual> Seats,
    double BoardWidth,
    double BoardHeight,
    int Version = 0,
    System.Collections.Generic.IReadOnlyList<BoardOverlay>? Overlays = null)
{
    /// <summary>空布局（未生成座位）。</summary>
    public static readonly SeatLayoutSnapshot Empty = new([], 0, 0);
}
