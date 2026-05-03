using System.Text.Json.Serialization;

namespace A_Pair.Core.Models
{
    /// <summary>
    /// 布局元数据基类，每种布局类型有其对应的派生元数据类。
    /// </summary>
    [JsonDerivedType(typeof(GridLayoutMetadata), "Grid")]
    [JsonDerivedType(typeof(PolarLayoutMetadata), "Polar")]
    [JsonDerivedType(typeof(FreeformLayoutMetadata), "Freeform")]
    public class LayoutMetadata { }

    /// <summary>
    /// 网格布局的元数据，定义行列数、间距、桌面配置、过道、教室特征等。
    /// </summary>
    public class GridLayoutMetadata : LayoutMetadata
    {
        /// <summary>总行数。</summary>
        public int Rows { get; set; }

        /// <summary>总列数。</summary>
        public int Columns { get; set; }

        /// <summary>水平间距（相邻列之间的基准距离，像素）。</summary>
        public double HorizontalSpacing { get; set; } = 1.0;

        /// <summary>垂直间距（相邻行之间的基准距离，像素）。</summary>
        public double VerticalSpacing { get; set; } = 1.0;

        /// <summary>原点 X 坐标（第一行第一列的左上角位置）。</summary>
        public double OriginX { get; set; } = 0.0;

        /// <summary>原点 Y 坐标。</summary>
        public double OriginY { get; set; } = 0.0;

        /// <summary>每桌座位数，默认 2。支持 1~6。</summary>
        public int SeatsPerDesk { get; set; } = 2;

        /// <summary>同桌内相邻座位间距（像素），默认 12。</summary>
        public double IntraDeskSpacing { get; set; } = 12.0;

        /// <summary>相邻桌边界间距（像素），默认 40。</summary>
        public double InterDeskSpacing { get; set; } = 40.0;

        /// <summary>哪些列索引后是过道（列从 1 开始计数）。如 {3,6} 表示第 3 列和第 6 列后有过道。</summary>
        public List<int> AisleAfterColumns { get; set; } = [];

        /// <summary>哪些行索引后是过道（行从 1 开始计数）。</summary>
        public List<int> AisleAfterRows { get; set; } = [];

        /// <summary>过道宽度（像素），默认 60。</summary>
        public double AisleWidth { get; set; } = 60.0;

        /// <summary>不放置桌子的网格位置列表。每项包含行列号。</summary>
        public List<GridPosition> EmptyPositions { get; set; } = [];

        /// <summary>前排的行数（从第 1 行开始计数），默认 1。</summary>
        public int FrontRowCount { get; set; } = 1;

        /// <summary>是否有讲台。</summary>
        public bool HasPodium { get; set; } = true;

        /// <summary>讲台宽度（像素），默认 60。</summary>
        public double PodiumWidth { get; set; } = 60.0;

        /// <summary>讲台高度（像素），默认 40。</summary>
        public double PodiumHeight { get; set; } = 40.0;

        /// <summary>是否有前门。</summary>
        public bool HasFrontDoor { get; set; } = false;
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
