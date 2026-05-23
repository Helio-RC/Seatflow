namespace A_Pair.Core.Models
{
    /// <summary>
    /// 会场文件 (.venue.json) 的数据结构。
    /// 包含会场标识和对应的教室布局定义。
    /// </summary>
    public class VenueFile
    {
        /// <summary>文件格式版本号。</summary>
        public string Version { get; set; } = "1.0";

        /// <summary>会场唯一标识符。</summary>
        public string VenueId { get; set; } = string.Empty;

        /// <summary>会场对应的教室布局定义。</summary>
        public ClassroomLayoutDefinition Layout { get; set; } = new();
    }
}