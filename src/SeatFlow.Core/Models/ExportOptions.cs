namespace SeatFlow.Core.Models
{
    /// <summary>
    /// 导出选项，控制座位安排结果的导出格式和行为。
    /// </summary>
    public class ExportOptions
    {
        /// <summary>导出格式（Excel / Csv / Pdf / Json）。</summary>
        public ExportFormat Format { get; set; } = ExportFormat.Excel;

        /// <summary>是否匿名化导出（学生姓名/ID 替换为 ***）。</summary>
        public bool Anonymize { get; set; }

        /// <summary>是否包含元数据（如导出时间、座位总数等）。</summary>
        public bool IncludeMetadata { get; set; }

        /// <summary>附加设置字典，供导出器扩展使用。</summary>
        public Dictionary<string, object> AdditionalSettings { get; set; } = [];

        /// <summary>导出视角（学生视角/教师视角）。默认学生视角。</summary>
        public LayoutPerspective Perspective { get; set; } = LayoutPerspective.StudentView;

        /// <summary>页眉标题（如「座位表  2026-10-03 00:36」）。为空时导出器回退为 <see cref="ExportTexts.SeatingChart"/>。</summary>
        public string? HeaderTitle { get; set; }

        /// <summary>页眉副标题（会场/名单/视角/完整生成时间）。为空时导出器回退为布局名。</summary>
        public string? HeaderSubtitle { get; set; }

        /// <summary>页脚小字（如「By SeatFlow v2.1.0」）。为空则不输出。</summary>
        public string? FooterNote { get; set; }

        /// <summary>导出文案（随 UI 语言注入）。</summary>
        public ExportTexts Texts { get; set; } = new();
    }

    /// <summary>
    /// 导出格式枚举。
    /// </summary>
    public enum ExportFormat
    {
        /// <summary>Excel (.xlsx)</summary>
        Excel,
        /// <summary>CSV 逗号分隔值</summary>
        Csv,
        /// <summary>PDF 文档</summary>
        Pdf,
        /// <summary>JSON 格式</summary>
        Json,
        /// <summary>PNG 图片</summary>
        Png
    }
}