using A_Pair.Core.Models;

namespace A_Pair.Infrastructure.Layouts
{
    /// <summary>
    /// 极坐标（环形）布局构建器，创建基于半径和角度的环形座位排列。
    /// </summary>
    /// <remarks>
    /// 生成 <see cref="LayoutType.Polar"/> 类型的布局，每个座位使用 <see cref="PolarSeat"/>
    /// 表示，包含半径（Radius）和角度（AngleDegrees）属性。
    /// 座位按环分布，每环座位均匀分布在圆周上。
    /// </remarks>
    public class PolarLayoutBuilder
    {
        /// <summary>
        /// 构建指定环数和每环座位数的极坐标布局。
        /// </summary>
        /// <param name="radiusStep">相邻环之间的半径步长。</param>
        /// <param name="rings">环的数量。</param>
        /// <param name="seatsPerRing">每环的座位数。</param>
        /// <returns>包含所有极坐标座位的布局定义。</returns>
        public static ClassroomLayoutDefinition BuildPolar (double radiusStep , int rings , int seatsPerRing)
        {
            var layout = new ClassroomLayoutDefinition
            {
                LayoutType = LayoutType.Polar ,
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