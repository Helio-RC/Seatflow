namespace A_Pair.Core.Models
{
    public class AppSettings
    {
        public WindowStateSettings WindowState { get; set; } = new();
        public string? LastOpenedFilePath { get; set; }
        public string? LastVenueId { get; set; }
        public List<string> RecentFiles { get; set; } = new();
    }

    public class WindowStateSettings
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; } = 1024;
        public double Height { get; set; } = 768;
        public bool IsMaximized { get; set; }
    }
}