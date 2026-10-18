namespace SeatFlow.Core.Models
{
    /// <summary>
    /// 会场轻量摘要（ID + 名称 + 布局类型 + 座位数）。
    /// 用于会场列表展示，避免为了取摘要信息而反序列化完整布局（大教室可达数百座位）。
    /// </summary>
    /// <param name="Id">会场唯一标识符（文件名主体）。</param>
    /// <param name="Name">会场显示名称；读取失败时回退为 ID。</param>
    public record VenueSummary(string Id, string Name)
    {
        /// <summary>布局类型（网格 / 极坐标 / 自由点）。</summary>
        public LayoutType LayoutType { get; init; } = LayoutType.Grid;

        /// <summary>座位总数。</summary>
        public int SeatCount { get; init; }
    }
}
