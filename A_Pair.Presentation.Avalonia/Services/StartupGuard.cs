using System;
using System.Runtime.InteropServices;
using System.Text;

namespace A_Pair.Presentation.Avalonia.Services;

internal static class StartupGuard
{
    public static (bool HasWarning, string Message) CheckEnvironment ()
    {
        var sb = new StringBuilder();
        var major = Environment.Version.Major;

        if (major < 10)
        {
            sb.AppendLine($"检测到 .NET 运行时版本 {Environment.Version}，建议使用 .NET 10.0 或更高版本。");
        }

        if (!IsSupportedOS())
        {
            sb.Append("当前操作系统 ");
            sb.Append(RuntimeInformation.OSDescription);
            sb.Append(" 未经完整测试，可能出现兼容性问题。");
        }

        var message = sb.ToString().TrimEnd();
        return (message.Length > 0, message);
    }

    private static bool IsSupportedOS ()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Environment.OSVersion.Version.Major >= 10;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Environment.OSVersion.Version.Major >= 12;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return true;
        return false;
    }
}
