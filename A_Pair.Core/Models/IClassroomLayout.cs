using System.Collections.Generic;

namespace A_Pair.Core.Models
{
    public interface IClassroomLayout
    {
        string Id { get; }
        string Name { get; }
        LayoutType LayoutType { get; }
        IReadOnlyList<Seat> Seats { get; }
        IReadOnlyList<Obstacle> Obstacles { get; }
        LayoutMetadata Metadata { get; }
    }

    public enum LayoutType
    {
        Grid,
        Polar,
        Freeform
    }
}