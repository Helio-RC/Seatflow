using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using SeatFlow.Application.Services;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Presentation.Avalonia.Views;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;

[assembly: System.Resources.NeutralResourcesLanguage("zh-CN")]

namespace SeatFlow.Presentation.Avalonia
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main (string[] args)
        {
#if !DEBUG
            CheckCleanDirectory();
#endif
            // 检测命令行参数中的 .seatsets 文件路径（双击打开或命令行导入）
            string? seatsetsFilePath = null;
            if (args.Length > 0)
            {
                var firstArg = args[0];
                if (!string.IsNullOrEmpty(firstArg) &&
                    firstArg.EndsWith(".seatsets" , StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(firstArg))
                {
                    seatsetsFilePath = Path.GetFullPath(firstArg);
                }
            }

            using var mutex = new Mutex(true , @"Global\SeatFlow_SeatingArrangement" , out bool isFirstInstance);

            var services = new ServiceCollection();
            services.AddSeatFlowApplication("AppData" , "Plugins");

            // 注册导航服务
            services.AddSingleton<INavigationService , NavigationService>();
            services.AddSingleton<IFileService , FileService>();
            services.AddSingleton<IDialogService , DialogService>();
            services.AddSingleton<WatchdogService>();

            // 注册 ViewModels
            services.AddSingleton<MainWindow>();
            services.AddSingleton<IOnboardingService , OnboardingService>();
            services.AddSingleton<IOnboardingStarter>(sp => (IOnboardingStarter)sp.GetRequiredService<IOnboardingService>());
            services.AddSingleton<MainShellViewModel>();
            services.AddSingleton<HomeViewModel>();
            services.AddSingleton<MemberManagementViewModel>();
            services.AddSingleton<VenueConfigurationViewModel>();
            services.AddSingleton<FreeformManagementViewModel>();
            services.AddSingleton<StrategyConfigurationViewModel>();
            services.AddSingleton<SeatingArrangementViewModel>();
            services.AddTransient<SnapshotHistoryViewModel>();
            services.AddSingleton<PluginManagementViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<AboutViewModel>();

            // 声明式策略配置子组件
            services.AddTransient<ConfigBlockEditorViewModel>();

            var serviceProvider = services.BuildServiceProvider();

            // 将命令行中的 .seatsets 文件路径传递给 App（用于双击打开导入）
            App.PendingSeatSetsFilePath = seatsetsFilePath;

            BuildAvaloniaApp(serviceProvider , isFirstInstance)
                .StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp (IServiceProvider serviceProvider , bool isFirstInstance)
            => AppBuilder.Configure(() => new App(serviceProvider , isFirstInstance))
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();

        private static void CheckCleanDirectory ()
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath)!;
            if (!Directory.Exists(exeDir)) return;

            var exeName = Path.GetFileName(Environment.ProcessPath);
            var extra = Directory.GetFileSystemEntries(exeDir)
                .Select(p => Path.GetFileName(p))
                .Where(n => !string.Equals(n , exeName , StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(n , "AppData" , StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(n , "Plugins" , StringComparison.OrdinalIgnoreCase)
                         && !n.EndsWith(".seatsets" , StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (extra.Length == 0) return;

            const int maxShow = 5;
            var display = extra.Take(maxShow).ToArray();
            var message = "程序所在目录不应有其他文件或文件夹。\n\n" +
                $"请将 {exeName} 单独放入一个空目录后再运行。\n\n" +
                $"当前目录多余项：\n{string.Join('\n' , display)}";

            if (extra.Length > maxShow)
                message += $"\n（还有 {extra.Length - maxShow} 项未显示）";

            ShowFatalDialog("运行环境错误" , message);
            Environment.Exit(1);
        }

        [DllImport("user32.dll" , CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW (IntPtr hWnd , string text , string caption , uint type);

        private static void ShowFatalDialog (string title , string message)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    _ = MessageBoxW(IntPtr.Zero , message , title , 0x00000010); // MB_ICONERROR
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start("zenity" , $"--error --width=480 --title=\"{title}\" --text=\"{message.Replace("\"" , "\\\"")}\"");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    var escaped = message.Replace("\\" , "\\\\").Replace("\"" , "\\\"").Replace("\n" , "\\n");
                    Process.Start("osascript" , $"-e \"display dialog \\\"{escaped}\\\" with title \\\"{title}\\\" buttons {{\\\"OK\\\"}} default button 1 with icon stop\"");
                }
                else
                {
                    Console.Error.WriteLine($"{title}: {message}");
                }
            }
            catch
            {
                Console.Error.WriteLine($"{title}: {message}");
            }
        }
    }
}
