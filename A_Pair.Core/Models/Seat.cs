using System;
using A_Pair.Core.Utilities;

namespace A_Pair.Core.Models
{
    public enum SeatType
    {
        Grid,
        Polar,
        Freeform
    }

    public abstract class Seat
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public abstract SeatType Type { get; }
        public string LogicalGroup { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonIgnore]
        public abstract ISeatGeometry GeometryData { get; }

        public bool IsAvailable { get; set; } = true;
        public bool IsFixed { get; set; }
        public string? OccupantId { get; set; }

        public AttributeBag Extensions { get; set; } = new();
    }

    public class GridSeat : Seat
    {
        public int Row { get; set; }
        public int Column { get; set; }

        public override SeatType Type => SeatType.Grid;
        public override ISeatGeometry GeometryData => new GridPosition { Row = Row, Column = Column };
    }

    public class PolarSeat : Seat
    {
        public double Radius { get; set; }
        public double AngleDegrees { get; set; }

        public override SeatType Type => SeatType.Polar;
        public override ISeatGeometry GeometryData => new PolarPosition { Radius = Radius, AngleDegrees = AngleDegrees };
    }
}
