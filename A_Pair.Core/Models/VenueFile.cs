namespace A_Pair.Core.Models
{
    public class VenueFile
    {
        public string Version { get; set; } = "1.0";
        public string VenueId { get; set; } = string.Empty;
        public ClassroomLayoutDefinition Layout { get; set; } = new();
    }
}