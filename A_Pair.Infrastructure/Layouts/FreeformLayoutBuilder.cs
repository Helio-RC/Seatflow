using A_Pair.Core.Models;

namespace A_Pair.Infrastructure.Layouts
{
    /// <summary>
    /// 自由形式布局构建器，根据指定的坐标点列表创建任意排列的座位布局。
    /// </summary>
    /// <remarks>
    /// 生成 <see cref="LayoutType.Freeform"/> 类型的布局，每个座位使用 <see cref="FreeformSeat"/>
    /// 表示，包含 X 和 Y 坐标属性。适用于不规则教室或自定义座位排列场景。
    /// </remarks>
    public class FreeformLayoutBuilder
    {
        /// <summary>
        /// 根据坐标点列表构建自由形式布局。
        /// </summary>
        /// <param name="points">座位坐标点集合，每个点包含 X 和 Y 坐标。</param>
        /// <returns>包含所有自由形式座位的布局定义。</returns>
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