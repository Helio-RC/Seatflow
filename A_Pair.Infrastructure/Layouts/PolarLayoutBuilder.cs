using A_Pair.Core.Models;

namespace A_Pair.Infrastructure.Layouts
{
    public class PolarLayoutBuilder
    {
        public static ClassroomLayoutDefinition BuildPolar (double radiusStep , int rings , int seatsPerRing)
        {
            var layout = new ClassroomLayoutDefinition
            {
                LayoutType = LayoutType.Polar ,   // 改为枚举值
                Metadata = new PolarLayoutMetadata { Rings = rings , SeatsPerRing = seatsPerRing , RadiusStep = radiusStep }
            };

            for (int ring = 1; ring <= rings; ring++)
            {
                double radius = ring * radiusStep;
                for (int i = 0; i < seatsPerRing; i++)
                {
                    double angle = 2 * Math.PI * i / seatsPerRing;
                    var seat = new PolarSeat { Radius = radius , AngleDegrees = angle * 180.0 / Math.PI };
                    layout.Seats.Add(seat);
                }
            }

            return layout;
        }
    }
}