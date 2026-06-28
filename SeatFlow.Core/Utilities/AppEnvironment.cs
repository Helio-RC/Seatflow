namespace SeatFlow.Core.Utilities;

/// <summary>
/// 应用程序环境信息帮助类。提供 exe 所在目录等运行时路径，
/// 避免各处重复使用 <see cref="AppContext.BaseDirectory"/>（单文件发布时指向临时解压目录）。
/// </summary>
public static class AppEnvironment
{
    /// <summary>
    /// exe 文件所在目录。使用 <see cref="Environment.ProcessPath"/> 而非
    /// <see cref="AppContext.BaseDirectory"/>，因为单文件发布（PublishSingleFile=true）时
    /// 后者指向临时解压目录（如 %TEMP%\.net\...），导致 AppData 等数据被创建到错误位置。
    /// <see cref="Environment.ProcessPath"/> 在 .NET 6+ 的正常进程中始终非 null。
    /// </summary>
    public static string ExeDirectory => Path.GetDirectoryName(Environment.ProcessPath)!;
}
