namespace A_Pair.Core.Models
{
    public class LayoutMetadata { }

    public class GridLayoutMetadata : LayoutMetadata
    {
        public int Rows { get; set; }
        public int Columns { get; set; }
        public double HorizontalSpacing { get; set; } = 1.0;
        public double VerticalSpacing { get; set; } = 1.0;
        public double OriginX { get; set; } = 0.0;
        public double OriginY { get; set; } = 0.0;
    }

    public class PolarLayoutMetadata : LayoutMetadata
    {
        public int Rings { get; set; } = 1;
        public int SeatsPerRing { get; set; } = 8;
        public double RadiusStep { get; set; } = 1.0;
        public double StartAngleDegrees { get; set; } = 0.0;
        public double OriginX { get; set; } = 0.0;
        public double OriginY { get; set; } = 0.0;
    }

    public class FreeformLayoutMetadata : LayoutMetadata
    {
        // Freeform relies on explicit points provided via ClassroomLayoutDefinition.Seats (as positions)
    }
}
