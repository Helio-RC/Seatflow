using A_Pair.Core.Models;

namespace A_Pair.Core.Strategies
{
    /// <summary>
    /// 共享的座位邻接判定逻辑，供 <see cref="DeskMateStrategy"/>、<see cref="NoRepeatDeskMateStrategy"/>
    /// 以及历史加载器使用。
    /// </summary>
    public static class SeatAdjacencyHelper
    {
        /// <summary>
        /// 判断两个座位是否几何相邻（不含桌边界或方向偏好限制）。
        /// 网格座位：同行左右相邻或同列上下相邻。
        /// 极坐标座位：同 LogicalGroup，或同环角度差 ≤45°，或相邻环角度几乎相同。
        /// 自由点座位：同 LogicalGroup，或欧几里得距离 ≤1.5。
        /// 混合类型座位不视为相邻。
        /// </summary>
        public static bool AreSeatsAdjacent (Seat a , Seat b)
        {
            if (a is GridSeat ga && b is GridSeat gb)
            {
                return (ga.Row == gb.Row && Math.Abs(ga.Column - gb.Column) == 1)
                    || (ga.Column == gb.Column && Math.Abs(ga.Row - gb.Row) == 1);
            }

            if (a is PolarSeat pa && b is PolarSeat pb)
            {
                // 优先：LogicalGroup 判定（由布局构建器设置，反映通道划分）
                bool hasGroups = !string.IsNullOrEmpty(pa.LogicalGroup)
                              && !string.IsNullOrEmpty(pb.LogicalGroup);
                if (hasGroups)
                    return pa.LogicalGroup == pb.LogicalGroup;

                // 回退：几何判定（无 LogicalGroup 的旧数据）
                const double angleTolerance = 1e-6;
                bool sameRing = Math.Abs(pa.Radius - pb.Radius) < 1e-6;
                if (sameRing)
                {
                    double raw = Math.Abs(pa.AngleDegrees - pb.AngleDegrees);
                    double angleDiff = Math.Min(raw , 360.0 - raw);
                    if (angleDiff <= 45.0) return true;
                }
                else
                {
                    double raw = Math.Abs(pa.AngleDegrees - pb.AngleDegrees);
                    double angleDiff = Math.Min(raw , 360.0 - raw);
                    if (angleDiff < angleTolerance)
                        return true;
                }
                return false;
            }

            if (a is FreeformSeat fa && b is FreeformSeat fb)
            {
                // 优先：LogicalGroup 判定（由布局构建器设置，反映分组划分）
                bool hasGroups = !string.IsNullOrEmpty(fa.LogicalGroup)
                              && !string.IsNullOrEmpty(fb.LogicalGroup);
                if (hasGroups)
                    return fa.LogicalGroup == fb.LogicalGroup;

                // 回退：欧几里得距离判定
                double dx = fa.X - fb.X;
                double dy = fa.Y - fb.Y;
                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                return distance <= 1.5;
            }

            return false;
        }

        /// <summary>
        /// 根据方向偏好和桌边界判断两个座位是否属于同一桌（同桌）。
        /// Grid 布局：根据 <paramref name="preferHorizontal"/> 和 <paramref name="allowVertical"/> 控制方向，
        /// 并通过 <paramref name="seatsPerDesk"/> 检查桌边界。
        /// 非 Grid 布局委托给 <see cref="AreSeatsAdjacent"/>。
        /// </summary>
        /// <param name="a">座位 A。</param>
        /// <param name="b">座位 B。</param>
        /// <param name="seatsPerDesk">每桌座位数（≥1）。大于 1 时检查同桌边界。</param>
        /// <param name="preferHorizontal">是否优先水平相邻（同行相邻列）。</param>
        /// <param name="allowVertical">是否允许垂直相邻（同列相邻行）。</param>
        public static bool AreDeskMates (
            Seat a , Seat b ,
            int seatsPerDesk ,
            bool preferHorizontal = true ,
            bool allowVertical = false)
        {
            // 非 Grid 布局使用通用 adjacency 判定（LogicalGroup / 几何距离）
            if (a is not GridSeat ga || b is not GridSeat gb)
                return AreSeatsAdjacent(a , b);

            bool horizontalOk = preferHorizontal
                && ga.Row == gb.Row && Math.Abs(ga.Column - gb.Column) == 1;
            bool verticalOk = allowVertical
                && ga.Column == gb.Column && Math.Abs(ga.Row - gb.Row) == 1;

            // 横向邻接时检查同桌边界：同一桌的座位必须在同一个 SeatsPerDesk 分组内
            if (horizontalOk && seatsPerDesk > 1)
            {
                int deskA = (ga.Column - 1) / seatsPerDesk;
                int deskB = (gb.Column - 1) / seatsPerDesk;
                if (deskA != deskB)
                    horizontalOk = false;
            }

            return horizontalOk || verticalOk;
        }
    }
}
