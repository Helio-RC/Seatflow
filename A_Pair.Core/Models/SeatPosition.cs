namespace A_Pair.Core.Models
{
    public interface ISeatGeometry { }

    public class GridPosition : ISeatGeometry
    {
        public int Row { get; set; }
        public int Column { get; set; }
    }

    public class PolarPosition : ISeatGeometry
    {
        public double Radius { get; set; }
        public double AngleDegrees { get; set; }
    }
}