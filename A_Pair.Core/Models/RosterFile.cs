namespace A_Pair.Core.Models
{
    /// <summary>
    /// .roster.json 文件结构
    /// </summary>
    public class RosterFile
    {
        public string Version { get; set; } = "1.0";
        public string? Description { get; set; }
        public List<Student> Students { get; set; } = [];
        public Dictionary<string , object> Metadata { get; set; } = [];
    }
}