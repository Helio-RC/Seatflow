using System.Collections.Generic;

namespace A_Pair.Core.Models
{
    public class ClassroomLayoutDefinition
    {
        public string LayoutType { get; set; } = "Grid";
        public LayoutMetadata Metadata { get; set; } = new LayoutMetadata();
        public List<Seat> Seats { get; set; } = new();
        public List<Obstacle> Obstacles { get; set; } = new();
    }
}
