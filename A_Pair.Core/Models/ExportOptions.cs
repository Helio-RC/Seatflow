namespace A_Pair.Core.Models
{
    public class ExportOptions
    {
        public ExportFormat Format { get; set; } = ExportFormat.Excel;
        public bool Anonymize { get; set; }
        public bool IncludeMetadata { get; set; }
        public Dictionary<string , object> AdditionalSettings { get; set; } = [];
    }

    public enum ExportFormat
    {
        Excel,
        Csv,
        Pdf,
        Json
    }
}