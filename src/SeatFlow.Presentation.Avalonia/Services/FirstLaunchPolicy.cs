namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 首启判定纯函数：首次安装、设置里的重跑标记、或本次自动导入数据包，三者任一成立即进入 OOBE。
/// </summary>
public static class FirstLaunchPolicy
{
    /// <param name="settingsFileMissing">AppSettings 文件是否不存在（真实首启）。</param>
    /// <param name="isFirstLaunchFlag">AppSettings.IsFirstLaunch 是否为 true（设置页重跑）。</param>
    /// <param name="seatSetsImported">本次启动是否自动导入了 .seatsets。</param>
    public static bool ShouldEnterOobe(
        bool settingsFileMissing,
        bool isFirstLaunchFlag,
        bool seatSetsImported)
        => settingsFileMissing || isFirstLaunchFlag || seatSetsImported;
}
