using System.Collections.Generic;
using A_Pair.Core.Models;

namespace A_Pair.Core.DomainServices
{
    public static class ObstacleProcessor
    {
        /// <summary>
        /// 将布局中与障碍物冲突的座位标记为不可用。
        /// </summary>
        public static void ApplyObstacles (ClassroomLayoutDefinition layout)
        {
            if (layout.Obstacles?.Count > 0)
            {
                foreach (var seat in layout.Seats)
                {
                    if (!seat.IsAvailable) continue; // 已不可用则跳过

                    var (px , py) = SeatGeometryHelper.GetPosition(seat , layout.Metadata);
                    if (IsInsideAnyObstacle(px , py , layout.Obstacles))
                    {
                        seat.IsAvailable = false;
                    }
                }
            }
        }

        private static bool IsInsideAnyObstacle (double x , double y , List<Obstacle> obstacles)
        {
            foreach (var obs in obstacles)
            {
                // 假定 Obstacle.X,Y 为左上角，Width 向右，Height 向下
                if (x >= obs.X && x <= obs.X + obs.Width &&
                    y >= obs.Y && y <= obs.Y + obs.Height)
                {
                    return true;
                }
            }
            return false;
        }
    }
}