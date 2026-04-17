using A_Pair.Core.Models;
using System.Collections.Generic;

namespace A_Pair.Infrastructure.Layouts
{
    public class FreeformLayoutBuilder
    {
        public static ClassroomLayoutDefinition BuildFreeform (IEnumerable<GridPosition> points)
        {
            var layout = new ClassroomLayoutDefinition
            {
                LayoutType = LayoutType.Freeform ,   // 改为枚举值
                Metadata = new FreeformLayoutMetadata()
            };

            foreach (var p in points)
            {
                var seat = new GridSeat { Row = p.Row , Column = p.Column };
                layout.Seats.Add(seat);
            }

            return layout;
        }
    }
}