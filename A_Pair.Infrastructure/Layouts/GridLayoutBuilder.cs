using A_Pair.Core.Models;  // 确保引用 LayoutType 枚举

namespace A_Pair.Infrastructure.Layouts
{
    public class GridLayoutBuilder
    {
        public static ClassroomLayoutDefinition BuildGrid (int rows , int columns)
        {
            var layout = new ClassroomLayoutDefinition
            {
                LayoutType = LayoutType.Grid ,   // 改为枚举值
                Metadata = new GridLayoutMetadata { Rows = rows , Columns = columns }
            };

            for (int r = 1; r <= rows; r++)
            {
                for (int c = 1; c <= columns; c++)
                {
                    var seat = new GridSeat { Row = r , Column = c };
                    layout.Seats.Add(seat);
                }
            }

            return layout;
        }
    }
}