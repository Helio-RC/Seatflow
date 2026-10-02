namespace SeatFlow.Core.Models
{
    /// <summary>
    /// 会场轻量摘要（ID + 名称）。
    /// 用于会场列表展示，避免为了取名称而反序列化完整布局（大教室可达数百座位）。
    /// </summary>
    /// <param name="Id">会场唯一标识符（文件名主体）。</param>
    /// <param name="Name">会场显示名称；读取失败时回退为 ID。</param>
    public record VenueSummary(string Id, string Name);
}
