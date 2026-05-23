namespace A_Pair.Contracts.Models;

/// <summary>
/// 插件视角的工作区契约，暴露排座策略所需的最小 API 表面。
/// Core 层的 <c>SeatingWorkspace</c> 实现此接口。
/// </summary>
public interface IPluginWorkspace
{
    /// <summary>学生列表（只读）。</summary>
    IReadOnlyList<IPluginStudent> Students { get; }

    /// <summary>尝试将学生分配到座位。</summary>
    bool TryAssignSeat (string seatId , string studentId , out string error);

    /// <summary>获取所有空座位。</summary>
    IEnumerable<IPluginSeat> GetEmptySeats ();

    /// <summary>按条件查找座位。</summary>
    IEnumerable<IPluginSeat> FindSeats (Func<IPluginSeat , bool> predicate);

    /// <summary>获取当前座位分配（座位 ID → 学生 ID）。</summary>
    IReadOnlyDictionary<string , string> GetAssignments ();
}
