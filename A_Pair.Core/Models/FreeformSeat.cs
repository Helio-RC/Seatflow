namespace A_Pair.Core.Models
{
    public class FreeformSeat : Seat
    {
        public double X { get; set; }
        public double Y { get; set; }

        public override SeatType Type => SeatType.Freeform;

        public override ISeatGeometry GeometryData => new FreeformPosition { X = X , Y = Y };
    }

    public class FreeformPosition : ISeatGeometry
    {
        public double X { get; set; }
        public double Y { get; set; }
    }
}