namespace A_Pair.Core.Models
{
    public class Obstacle
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Type { get; set; } = string.Empty;
    }
}