using A_Pair.Core.Models;

namespace A_Pair.Core.DomainServices
{
    public static class SeatGeometryHelper
    {
        /// <summary>
        /// 根据座位类型和布局元数据计算座位的物理坐标 (X, Y)。
        /// </summary>
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

        private static (double X , double Y) GetGridPosition (GridSeat seat , GridLayoutMetadata? gridMeta)
        {
            if (gridMeta == null)
                throw new ArgumentException("Grid seat requires GridLayoutMetadata.");

            double x = gridMeta.OriginX + (seat.Column - 1) * gridMeta.HorizontalSpacing;
            double y = gridMeta.OriginY + (seat.Row - 1) * gridMeta.VerticalSpacing;
            return (x , y);
        }

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