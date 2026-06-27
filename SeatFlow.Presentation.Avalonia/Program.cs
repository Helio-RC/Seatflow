using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using SeatFlow.Application.Services;
using SeatFlow.Core.Models.SeatSets;
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
        /// <summary>用于将 .seatsets 文件路径从第二个进程转发到第一个进程的命名管道名称。</summary>
        internal const string SeatSetsPipeName = "SeatFlow_SeatSetsPipe";

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

            // Windows: 注册 .seatsets 文件关联，使双击文件能启动程序导入
            RegisterSeatSetsFileAssociation();

            // 如果是非首个实例且有 .seatsets 文件，转发给已有实例后静默退出
            if (seatsetsFilePath != null && TryForwardToExistingInstance(seatsetsFilePath))
                return;

            // 在 DI 创建 AppData/Logs 之前扫描自动导入文件
            // AddSeatFlowApplication 会创建 Logs 目录使 AppData 存在，导致后续检查失效
            var autoImportPath = DiscoverAutoImportSeatSetsFile();

            using var mutex = new Mutex(true , @"Global\SeatFlow_SeatingArrangement" , out bool isFirstInstance);

            var services = new ServiceCollection();
            // 使用绝对路径，避免双击 .seatsets 文件时工作目录被 Windows 改为文件所在位置
            services.AddSeatFlowApplication(
                Path.Combine(AppContext.BaseDirectory , "AppData") ,
                Path.Combine(AppContext.BaseDirectory , "Plugins"));

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

            // 将自动发现的 .seatsets 文件路径传递给 App（用于首次启动数据恢复）
            App.AutoImportSeatSetsPath = autoImportPath;

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

        /// <summary>
        /// 在 HKCU 中注册 .seatsets 文件关联（仅 Windows，无需管理员权限）。
        /// 注册后双击 .seatsets 文件会自动启动程序并导入。
        /// 幂等操作——重复调用不会产生副作用。
        /// </summary>
        private static void RegisterSeatSetsFileAssociation ()
        {
            if (!OperatingSystem.IsWindows())
                return;

            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                    return;

                var progId = "SeatFlow.seatsets";

                using var extKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Classes\.seatsets");
                if (extKey.GetValue("") as string != progId)
                    extKey.SetValue("" , progId);

                using var progKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    $@"Software\Classes\{progId}");
                progKey.SetValue("" , "SeatFlow Data Package");

                using var iconKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    $@"Software\Classes\{progId}\DefaultIcon");
                iconKey.SetValue("" , $"\"{exePath}\",0");

                using var cmdKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    $@"Software\Classes\{progId}\shell\open\command");
                cmdKey.SetValue("" , $"\"{exePath}\" \"%1\"");
            }
            catch
            {
                // 注册失败静默处理——不影响正常启动
            }
        }

        /// <summary>
        /// 尝试通过命名管道将 .seatsets 文件路径转发给已有实例。
        /// 成功返回 true（调用方应静默退出），失败返回 false（可能是首个实例）。
        /// </summary>
        /// <summary>
        /// 在 AppData 目录创建之前扫描 exe 目录中的 .seatsets 文件用于自动导入。
        /// 仅在 AppData 不存在时生效（即真正的首次启动）。
        /// 扫描时进行轻量前置校验：大小合理、可解析为 JSON、含 formatVersion 字段。
        /// </summary>
        private static string? DiscoverAutoImportSeatSetsFile ()
        {
            try
            {
                var exeDir = AppContext.BaseDirectory;
                var appDataPath = Path.Combine(exeDir , "AppData");
                if (Directory.Exists(appDataPath))
                    return null;

                var files = Directory.GetFiles(exeDir , "*.seatsets" , SearchOption.TopDirectoryOnly);
                if (files.Length == 0)
                    return null;

                // 按修改时间降序，取第一个通过前置校验的文件
                var candidates = files
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc);

                foreach (var file in candidates)
                {
                    // 大小检查：最少 50 字节（一个最小合法 JSON），最大 200 MB
                    if (file.Length < 50 || file.Length > SeatSetsConstants.MaxFileSizeBytes)
                        continue;

                    // 轻量 JSON 结构检查：确认是合法 JSON 且含 formatVersion
                    try
                    {
                        var json = File.ReadAllText(file.FullName);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                            continue;
                        if (!root.TryGetProperty("formatVersion" , out _))
                            continue;
                        // 格式版本可解析
                    }
                    catch
                    {
                        continue; // JSON 解析失败，跳过
                    }

                    return file.FullName;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryForwardToExistingInstance (string filePath)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    "." , SeatSetsPipeName , PipeDirection.Out);
                // 短超时——如果连接不上说明没有已有实例在监听
                client.Connect(2000);
                using var writer = new StreamWriter(client);
                writer.WriteLine(filePath);
                writer.Flush();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
