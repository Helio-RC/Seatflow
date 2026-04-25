using A_Pair.Core.Models;

namespace A_Pair.Infrastructure.Layouts
{
    public class FreeformLayoutBuilder
    {
        // 改用专门的 FreeformSeat
        public static ClassroomLayoutDefinition BuildFreeform (IEnumerable<(double X , double Y)> points)
        {
            var layout = new ClassroomLayoutDefinition
            {
                LayoutType = LayoutType.Freeform ,
                Metadata = new FreeformLayoutMetadata()
            };

            foreach (var (x , y) in points)
            {
                var seat = new FreeformSeat { X = x , Y = y };
                layout.Seats.Add(seat);
            }

            return layout;
        }
    }
}