using System.Collections.Generic;

namespace A_Pair.Core.Models
{
    public class LayoutMetadata { }

    public class Obstacle { }

    public class ClassroomLayoutDefinition
    {
        public string LayoutType { get; set; } = "Grid";
        public LayoutMetadata Metadata { get; set; } = new();
        public List<Seat> Seats { get; set; } = new();
        public List<Obstacle> Obstacles { get; set; } = new();
    }
}
