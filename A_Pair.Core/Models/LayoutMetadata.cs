namespace A_Pair.Core.Models
{
    /// <summary>
    /// 布局元数据基类，每种布局类型有其对应的派生元数据类。
    /// </summary>
    public class LayoutMetadata { }

    /// <summary>
    /// 网格布局的元数据，定义行列数、间距和原点坐标。
    /// </summary>
    public class GridLayoutMetadata : LayoutMetadata
    {
        /// <summary>总行数。</summary>
        public int Rows { get; set; }

        /// <summary>总列数。</summary>
        public int Columns { get; set; }

        /// <summary>水平间距（相邻列之间的距离）。</summary>
        public double HorizontalSpacing { get; set; } = 1.0;

        /// <summary>垂直间距（相邻行之间的距离）。</summary>
        public double VerticalSpacing { get; set; } = 1.0;

        /// <summary>原点 X 坐标（第一行第一列的左上角位置）。</summary>
        public double OriginX { get; set; } = 0.0;

        /// <summary>原点 Y 坐标。</summary>
        public double OriginY { get; set; } = 0.0;
    }

    /// <summary>
    /// 极坐标布局的元数据，定义环数、每环座位数、半径步长和原点坐标。
    /// </summary>
    public class PolarLayoutMetadata : LayoutMetadata
    {
        /// <summary>环数。</summary>
        public int Rings { get; set; } = 1;

        /// <summary>每环的座位数。</summary>
        public int SeatsPerRing { get; set; } = 8;

        /// <summary>相邻环之间的半径增量。</summary>
        public double RadiusStep { get; set; } = 1.0;

        /// <summary>起始角度（度），0° 为右侧水平方向。</summary>
        public double StartAngleDegrees { get; set; } = 0.0;

        /// <summary>原点 X 坐标。</summary>
        public double OriginX { get; set; } = 0.0;

        /// <summary>原点 Y 坐标。</summary>
        public double OriginY { get; set; } = 0.0;
    }

    /// <summary>
    /// 自由点布局的元数据。
    /// 自由点布局依赖 <see cref="ClassroomLayoutDefinition.Seats"/> 中提供的显式坐标点，
    /// 因此元数据本身无需额外参数。
    /// </summary>
    public class FreeformLayoutMetadata : LayoutMetadata
    {
    }
}
