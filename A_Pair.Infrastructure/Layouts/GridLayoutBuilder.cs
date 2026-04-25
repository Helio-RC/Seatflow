using A_Pair.Core.Models;

namespace A_Pair.Infrastructure.Layouts
{
    /// <summary>
    /// 网格布局构建器，创建基于行和列的矩形座位排列。
    /// </summary>
    /// <remarks>
    /// 生成 <see cref="LayoutType.Grid"/> 类型的布局，每个座位使用 <see cref="GridSeat"/>
    /// 表示，包含行号（Row）和列号（Column）属性。元数据记录总行数和总列数。
    /// </remarks>
    public class GridLayoutBuilder
    {
        /// <summary>
        /// 构建指定行数和列数的网格布局。
        /// </summary>
        /// <param name="rows">行数。</param>
        /// <param name="columns">列数。</param>
        /// <returns>包含所有网格座位的布局定义。</returns>
        public static ClassroomLayoutDefinition BuildGrid (int rows , int columns)
        {
            var layout = new ClassroomLayoutDefinition
            {
                LayoutType = LayoutType.Grid ,
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