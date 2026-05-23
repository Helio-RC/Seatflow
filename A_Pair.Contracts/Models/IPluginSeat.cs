namespace A_Pair.Contracts.Models;

/// <summary>
/// 插件视角的座位只读视图。
/// </summary>
public interface IPluginSeat
{
    string Id { get; }
    bool IsAvailable { get; }
    bool IsFixed { get; }
    string? OccupantId { get; }
}
