namespace SeatFlow.Core.Models
{
    /// <summary>
    /// 导出文案集合。由 UI 层按当前语言注入；默认中文，保证 Core 在无 UI 时可独立导出。
    /// </summary>
    public class ExportTexts
    {
        /// <summary>座位表标题（页眉回退值）。</summary>
        public string SeatingChart { get; set; } = "座位表";

        /// <summary>未分配座位文案。</summary>
        public string Unassigned { get; set; } = "未分配";

        /// <summary>讲台文案。</summary>
        public string Podium { get; set; } = "讲台";

        /// <summary>门文案（仅一扇门时）。</summary>
        public string Door { get; set; } = "门";

        /// <summary>多扇门时的编号格式，参数为门序号。</summary>
        public string DoorNumberFormat { get; set; } = "门 #{0}";

        /// <summary>页眉信息「标签 + 值」之间的分隔符。</summary>
        public string LabelSeparator { get; set; } = "：";

        /// <summary>页眉各段信息之间的分隔符。</summary>
        public string InfoSeparator { get; set; } = "  ｜  ";

        /// <summary>同一行多扇门标签之间的分隔符。</summary>
        public string DoorSeparator { get; set; } = "、";
    }
}
