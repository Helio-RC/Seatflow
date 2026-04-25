namespace A_Pair.Core.Models
{
    /// <summary>
    /// 应用程序全局设置，持久化存储于 AppSettings.json 文件。
    /// 包含窗口状态、最近文件记录等用户偏好。
    /// </summary>
    public class AppSettings
    {
        /// <summary>窗口位置与大小设置。</summary>
        public WindowStateSettings WindowState { get; set; } = new();

        /// <summary>上次打开的文件路径。</summary>
        public string? LastOpenedFilePath { get; set; }

        /// <summary>上次使用的会场 ID。</summary>
        public string? LastVenueId { get; set; }

        /// <summary>最近打开的文件列表，用于"最近使用"快捷入口。</summary>
        public List<string> RecentFiles { get; set; } = [];
    }

    /// <summary>
    /// 窗口状态设置，记录主窗口的位置、大小和最大化状态。
    /// </summary>
    public class WindowStateSettings
    {
        /// <summary>窗口左上角 X 坐标。</summary>
        public double Left { get; set; }

        /// <summary>窗口左上角 Y 坐标。</summary>
        public double Top { get; set; }

        /// <summary>窗口宽度。</summary>
        public double Width { get; set; } = 1024;

        /// <summary>窗口高度。</summary>
        public double Height { get; set; } = 768;

        /// <summary>是否最大化。</summary>
        public bool IsMaximized { get; set; }
    }
}