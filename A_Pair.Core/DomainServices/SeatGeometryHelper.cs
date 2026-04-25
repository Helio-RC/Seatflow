using A_Pair.Core.Models;

namespace A_Pair.Core.DomainServices
{
    /// <summary>
    /// 座位几何辅助类，根据座位类型和布局元数据计算座位的物理坐标。
    /// 用于障碍物检测、渲染定位等需要物理坐标的场景。
    /// </summary>
    public static class SeatGeometryHelper
    {
        /// <summary>
        /// 根据座位类型和布局元数据计算座位的物理坐标 (X, Y)。
        /// 支持 GridSeat（网格）、PolarSeat（极坐标）和 FreeformSeat（自由点）三种类型。
        /// </summary>
        /// <param name="seat">座位实例。</param>
        /// <param name="metadata">布局元数据，必须与座位类型匹配。</param>
        /// <returns>物理坐标 (X, Y)。</returns>
        /// <exception cref="ArgumentException">座位类型不支持或元数据不匹配时抛出。</exception>
        public static (double X , double Y) GetPosition (Seat seat , LayoutMetadata metadata)
        {
            return seat switch
            {
                GridSeat grid => GetGridPosition(grid , metadata as GridLayoutMetadata),
                PolarSeat polar => GetPolarPosition(polar , metadata as PolarLayoutMetadata),
                FreeformSeat free => (free.X , free.Y),
                _ => throw new ArgumentException($"Unsupported seat type: {seat.GetType().Name}")
            };
        }

        /// <summary>
        /// 计算网格座位的物理坐标。
        /// 公式：X = OriginX + (Column - 1) × HorizontalSpacing
        ///       Y = OriginY + (Row - 1) × VerticalSpacing
        /// </summary>
        private static (double X , double Y) GetGridPosition (GridSeat seat , GridLayoutMetadata? gridMeta)
        {
            if (gridMeta == null)
                throw new ArgumentException("Grid seat requires GridLayoutMetadata.");

            double x = gridMeta.OriginX + (seat.Column - 1) * gridMeta.HorizontalSpacing;
            double y = gridMeta.OriginY + (seat.Row - 1) * gridMeta.VerticalSpacing;
            return (x , y);
        }

        /// <summary>
        /// 计算极坐标座位的物理坐标。
        /// 公式：X = OriginX + Radius × cos(Angle)
        ///       Y = OriginY + Radius × sin(Angle)
        /// 角度从度转换为弧度进行计算。
        /// </summary>
        private static (double X , double Y) GetPolarPosition (PolarSeat seat , PolarLayoutMetadata? polarMeta)
        {
            if (polarMeta == null)
                throw new ArgumentException("Polar seat requires PolarLayoutMetadata.");

            double rad = seat.AngleDegrees * Math.PI / 180.0;
            double x = polarMeta.OriginX + seat.Radius * Math.Cos(rad);
            double y = polarMeta.OriginY + seat.Radius * Math.Sin(rad);
            return (x , y);
        }
    }
}