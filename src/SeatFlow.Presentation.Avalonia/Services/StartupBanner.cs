using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SeatFlow.Core.Storage;
using SeatFlow.Infrastructure.Storage;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 启动自述：在日志最开头写入一条应用自述（版本/构建/运行环境/数据目录/启动时刻）。
/// 桌面写入 Serilog 文件日志，浏览器写入 DevTools Console。
/// 固定中文输出、不依赖界面语言，便于用户反馈问题时直接复制。
/// </summary>
internal static class StartupBanner
{
    /// <summary>
    /// 写入启动自述。任何异常都被吞掉——自述失败绝不能影响应用启动。
    /// 调用点：两个壳的 Program.Main 中 <c>BuildServiceProvider()</c> 之后
    /// （日志 Provider 已注册、任何业务日志之前）。
    /// </summary>
    internal static void Write(IServiceProvider services)
    {
        try
        {
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger("SeatFlow.Startup");
            if (logger is null)
                return;

            logger.LogInformation("{Banner}", Format(new StartupBannerInfo(
                Version: VersionInfo.Version,
                ReleaseTag: VersionInfo.ReleaseTag,
                CommitId: VersionInfo.CommitId,
                BuildDate: VersionInfo.BuildDate,
                Runtime: RuntimeInformation.FrameworkDescription,
                OS: GetOsDescription(),
                Architecture: GetArchitecture(),
                DataDirectory: ResolveDataDirectory(services),
                StartedAt: DateTimeOffset.Now)));
        }
        catch
        {
            // 自述失败静默处理，不影响启动
        }
    }

    /// <summary>格式化单行中文自述（纯函数，便于测试）。</summary>
    internal static string Format(StartupBannerInfo info)
        => $"SeatFlow 启动自述 | 版本 {info.Version}（{info.ReleaseTag}）| 提交 {info.CommitId} | " +
           $"构建 {info.BuildDate} | 运行时 {info.Runtime} | OS {info.OS} | 架构 {info.Architecture} | " +
           $"数据目录 {info.DataDirectory} | 启动时刻 {info.StartedAt:yyyy-MM-dd HH:mm:ss zzz}";

    /// <summary>数据目录：桌面 = 文件系统存储根目录；浏览器 = IndexedDB 标识。</summary>
    private static string ResolveDataDirectory(IServiceProvider services)
    {
        if (OperatingSystem.IsBrowser())
            return "IndexedDB";

        return services.GetService<ILocalDataStore>() is FileSystemDataStore store
            ? store.RootDirectory
            : "未知";
    }

    private static string GetOsDescription()
    {
        try { return RuntimeInformation.OSDescription; }
        catch { return "未知"; }
    }

    private static string GetArchitecture()
    {
        try { return RuntimeInformation.OSArchitecture.ToString(); }
        catch { return "未知"; }
    }
}

/// <summary>启动自述字段集合。</summary>
internal sealed record StartupBannerInfo(
    string Version,
    string ReleaseTag,
    string CommitId,
    string BuildDate,
    string Runtime,
    string OS,
    string Architecture,
    string DataDirectory,
    DateTimeOffset StartedAt);
