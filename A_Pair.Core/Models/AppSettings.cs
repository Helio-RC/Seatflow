using System.Text.Json.Serialization;

namespace A_Pair.Core.Models
{
    /// <summary>
    /// 应用程序全局设置，持久化存储于 AppSettings.json 文件。
    /// </summary>
    public class AppSettings
    {
        /// <summary>窗口位置与大小设置。</summary>
        public WindowStateSettings WindowState { get; set; } = new();

        /// <summary>主题模式。</summary>
        public ThemeMode Theme { get; set; } = ThemeMode.System;

        /// <summary>界面语言，为空时跟随系统。</summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>用户数据目录路径，为空时使用默认位置。</summary>
        public string DataDirectory { get; set; } = string.Empty;

        /// <summary>自动保存间隔（秒），0 表示禁用。</summary>
        public int AutoSaveIntervalSeconds { get; set; } = 300;

        /// <summary>最近打开的文件列表。</summary>
        public List<string> RecentFiles { get; set; } = [];

        /// <summary>上次打开的文件路径。</summary>
        public string? LastOpenedFilePath { get; set; }

        /// <summary>上次使用的会场 ID。</summary>
        public string? LastVenueId { get; set; }

        /// <summary>是否在清除数据前弹出确认对话框。</summary>
        public bool ConfirmBeforeClear { get; set; } = true;

        /// <summary>座位图默认缩放比例。</summary>
        public double DefaultZoomLevel { get; set; } = 1.0;

        /// <summary>日志配置（不暴露到设置 UI）。</summary>
        public LogSettings Logging { get; set; } = new();

    }

    /// <summary>
    /// 日志配置，存放于 AppSettings.json 中，不在设置页面展示。
    /// </summary>
    public sealed class LogSettings
    {
        /// <summary>最低日志级别：Debug / Information / Warning / Error。</summary>
        public string MinimumLevel { get; set; } = "Information";

        /// <summary>单个日志文件最大字节数，默认 10MB。</summary>
        public long FileSizeLimitBytes { get; set; } = 10_485_760;

        /// <summary>保留的日志文件数量上限。</summary>
        public int RetainedFileCountLimit { get; set; } = 30;
    }

    /// <summary>
    /// 窗口状态设置。
    /// </summary>
    public class WindowStateSettings
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; } = 1024;
        public double Height { get; set; } = 768;
        public bool IsMaximized { get; set; }
    }

    /// <summary>
    /// 主题模式枚举。
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ThemeMode
    {
        /// <summary>跟随系统主题。</summary>
        System,
        /// <summary>浅色主题。</summary>
        Light,
        /// <summary>深色主题。</summary>
        Dark
    }

}
