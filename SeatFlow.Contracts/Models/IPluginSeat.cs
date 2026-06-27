namespace SeatFlow.Contracts.Models;

/// <summary>
/// 插件视角的座位视图。插件可通过设置 <see cref="IsFixed"/> 为 true
/// 来保护已分配的座位不被后续"扫地"策略（如碎片整理）移动。
/// </summary>
public interface IPluginSeat
{
    string Id { get; }
    bool IsAvailable { get; }
    bool IsFixed { get; set; }
    string? OccupantId { get; }
}
