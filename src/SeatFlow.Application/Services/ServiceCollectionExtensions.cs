using System.Text.Json;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Exporters;
using SeatFlow.Core.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Core.Services;
using SeatFlow.Core.Storage;
using SeatFlow.Core.Strategies;
using SeatFlow.Infrastructure.Exporters;
using SeatFlow.Infrastructure.Migration;
using SeatFlow.Infrastructure.Migration.Migrators;
using SeatFlow.Infrastructure.Providers;
using SeatFlow.Infrastructure.Repositories;
using SeatFlow.Infrastructure.Serialization;
using SeatFlow.Infrastructure.Services;
using SeatFlow.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace SeatFlow.Application.Services
{
    /// <summary>
    /// 提供 <see cref="IServiceCollection"/> 的扩展方法，用于注册 SeatFlow 应用程序层的所有服务。
    /// </summary>
    /// <remarks>
    /// 此扩展方法一次性注册以下组件：
    /// <list type="bullet">
    ///   <item><see cref="IApplicationFacade"/> — 应用程序外观</item>
    ///   <item>内置策略（<see cref="FixedSeatStrategy"/>、<see cref="RandomFillStrategy"/>、<see cref="FrontRowRotationStrategy"/> 作为 <see cref="ISeatingStrategy"/>）</item>
    ///   <item>依赖策略（<see cref="DeskMateStrategy"/> 作为 <see cref="IDependentSeatingStrategy"/>，在 RandomFill 上下文中执行）</item>
    ///   <item>导出器（Excel、CSV、PDF）</item>
    ///   <item>学生写入器（JSON、CSV、XLSX）</item>
    ///   <item><see cref="IConflictResolver"/> — 冲突解决器</item>
    ///   <item><see cref="IVenueRepository"/> 与 <see cref="IAppSettingsRepository"/> — 数据持久化</item>
    ///   <item><see cref="SeatingSnapshotRepository"/> — 快照存储</item>
    /// </list>
    /// </remarks>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 将 SeatFlow 应用程序层的所有服务注册到依赖注入容器中。
        /// 启动时读取默认位置的 AppSettings.json，若设置了 <c>DataDirectory</c>，
        /// 则使用自定义路径作为所有数据的基目录。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="snapshotBasePath">数据存储的默认基路径。</param>
        /// <returns>服务集合，支持链式调用。</returns>
        public static IServiceCollection AddSeatFlowApplication(this IServiceCollection services, string snapshotBasePath)
        {
            // 解析有效数据目录 + 读取日志配置（单次 I/O）
            var defaultSettingsPath = Path.Combine(snapshotBasePath, "AppSettings.json");
            var effectiveDataPath = snapshotBasePath;
            var logSettings = new LogSettings();
            try
            {
                if (File.Exists(defaultSettingsPath))
                {
                    var json = File.ReadAllText(defaultSettingsPath);
                    var existing = JsonSerializer.Deserialize<AppSettings>(json,
                        JsonOptions.CaseInsensitiveRead);
                    if (existing?.DataDirectory is { Length: > 0 } customPath && Directory.Exists(customPath))
                        effectiveDataPath = customPath;
                    if (existing?.Logging is { } ls)
                        logSettings = ls;
                }
            }
            catch { /* 读取失败时使用默认值 */ }

            var logLevel = ParseLogLevel(logSettings.MinimumLevel);

            // 调试运行时自动提升日志详细度，无需修改配置文件
            if (System.Diagnostics.Debugger.IsAttached && logLevel > LogEventLevel.Debug)
                logLevel = LogEventLevel.Debug;
            var logDir = Path.Combine(effectiveDataPath, "Logs");
            Directory.CreateDirectory(logDir);

            // 清理超出保留数量的旧日志文件
            PruneOldLogFiles(logDir, logSettings.RetainedFileCountLimit);

            // 实例隔离：每次启动创建独立日志文件，避免多实例写入冲突
            var instanceId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var logPath = Path.Combine(logDir, $"SeatFlow_{instanceId}.log");
            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Is(logLevel)
                .Enrich.WithThreadId();

            // 应用分模块日志等级覆盖（用户配置的 Key 去掉 "SeatFlow." 前缀）
            foreach (var (category, levelStr) in logSettings.CategoryOverrides)
            {
                if (!string.IsNullOrWhiteSpace(category))
                {
                    var catLevel = ParseLogLevel(levelStr);
                    loggerConfig.MinimumLevel.Override($"SeatFlow.{category}", catLevel);
                }
            }

            // 调试模式下在输出中携带线程 ID，便于诊断并发问题
            var outputTemplate = logLevel <= LogEventLevel.Debug
                ? "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level}] [{SourceContext}] [Thread:{ThreadId}] {Message:lj}{NewLine}{Exception}"
                : "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

            Log.Logger = loggerConfig
                .WriteTo.File(
                    logPath,
                    outputTemplate: outputTemplate,
                    fileSizeLimitBytes: logSettings.FileSizeLimitBytes,
                    retainedFileCountLimit: logSettings.RetainedFileCountLimit,
                    rollOnFileSizeLimit: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(5))
                .CreateLogger();

            return RegisterServices(services,
                new FileSystemDataStore(effectiveDataPath),
                defaultAppSettingsPath: defaultSettingsPath,
                withFileLogging: true);
        }

        /// <summary>
        /// 以存储抽象注册全部服务（WASM/浏览器路径）。
        /// 不配置 Serilog 文件日志（浏览器无文件系统）；日志走默认 provider。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="store">数据存储实现（桌面 = 文件系统，WASM = IndexedDB）。</param>
        public static IServiceCollection AddSeatFlowApplication(this IServiceCollection services, ILocalDataStore store)
            => RegisterServices(services, store, defaultAppSettingsPath: null, withFileLogging: false);

        /// <summary>
        /// 注册全部策略/仓库/提供器服务。桌面与浏览器共用；差异点：
        /// <list type="bullet">
        /// <item>AppSettings 仓储：桌面传绝对路径（兼容 App.axaml.cs 的 File.Exists 检查），浏览器传相对路径。</item>
        /// <item>文件日志：仅桌面（Serilog File sink），浏览器不配置。</item>
        /// </list>
        /// </summary>
        private static IServiceCollection RegisterServices(
            IServiceCollection services,
            ILocalDataStore store,
            string? defaultAppSettingsPath,
            bool withFileLogging)
        {
            if (withFileLogging)
                services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));
            else
                services.AddLogging(); // 浏览器：无 sink（ILogger<T> 可解析，默认空实现）
            services.AddSingleton(store);
            services.AddSingleton(sp => new CsvStudentProvider(sp.GetRequiredService<ILocalDataStore>()));
            services.AddSingleton(sp => new XlsxStudentProvider(sp.GetRequiredService<ILocalDataStore>()));
            services.AddSingleton(sp => new JsonStudentProvider(sp.GetRequiredService<ILocalDataStore>()));
            services.TryAddSingleton<IStudentProvider, CompositeStudentProvider>();
            services.AddSingleton<FileMigrationService>();
            services.AddSingleton<IFileMigrator, VenueMigrators.Step_1_0_to_1_1>();
            services.AddSingleton<IFileMigrator, SeatSetsMigrators.Step_1_0_to_1_1>();
            services.AddSingleton<ISeatingSnapshotRepository>(sp =>
                new SeatingSnapshotRepository(store, "Assignments",
                    sp.GetRequiredService<FileMigrationService>(),
                    sp.GetRequiredService<ILogger<SeatingSnapshotRepository>>()));
            services.AddSingleton<FrontRowHistoryLoader>();
            services.AddSingleton<NoRepeatDeskMateHistoryLoader>();
            services.AddSingleton<IApplicationFacade, ApplicationFacade>();


            // 注册内置策略（工厂方法注入 ILogger<T>）
            services.AddSingleton<ISeatingStrategy>(sp => new FixedSeatStrategy(
                new FixedSeatConfiguration(), sp.GetRequiredService<ILogger<FixedSeatStrategy>>()));
            services.AddSingleton<ISeatingStrategy>(sp => new RandomFillStrategy(
                new Random(), sp.GetRequiredService<ILogger<RandomFillStrategy>>()));
            services.AddSingleton<ISeatingStrategy>(sp => new FrontRowRotationStrategy(
                new FrontRowRotationStrategy.FrontRowRotationConfiguration(), sp.GetRequiredService<ILogger<FrontRowRotationStrategy>>()));

            // 注册 Defrag 策略（Priority=0，在 RandomFill 之后最后执行）
            services.AddSingleton<ISeatingStrategy>(sp => new DefragStrategy(
                new DefragConfiguration(), sp.GetRequiredService<ILogger<DefragStrategy>>()));

            // 注册依赖策略（在 RandomFill 上下文中执行）
            services.AddSingleton<IDependentSeatingStrategy>(sp => new DeskMateStrategy(
                new DeskMateConfiguration(), sp.GetRequiredService<ILogger<DeskMateStrategy>>()));
            services.AddSingleton<IDependentSeatingStrategy>(sp => new GenderRestrictedSeatStrategy(
                new GenderRestrictedSeatConfiguration(), sp.GetRequiredService<ILogger<GenderRestrictedSeatStrategy>>()));
            services.AddSingleton<IDependentSeatingStrategy>(sp => new NoRepeatDeskMateStrategy(
                new NoRepeatDeskMateConfiguration(), sp.GetRequiredService<ILogger<NoRepeatDeskMateStrategy>>()));

            // 注册导出器（browser 目标不注册 PDF/图片导出器：对应库在 WASM 不可用）
#if !BROWSER
            services.AddSingleton<ISeatingPlanExporter, PdfSeatingExporter>();
            services.AddSingleton<ISeatingPlanExporter, ImageSeatingExporter>();
#endif
            services.AddSingleton<ISeatingPlanExporter, ExcelSeatingExporter>();
            services.AddSingleton<ISeatingPlanExporter, CsvSeatingExporter>();
            services.AddTransient<IStudentWriter, JsonStudentWriter>();
            services.AddTransient<IStudentWriter, CsvStudentWriter>();
#if !BROWSER
            services.AddTransient<IStudentWriter, XlsxStudentWriter>();
#else
            // WASM：XLSX 写入学籍（EPPlus 已验证可用），注册保留
            services.AddTransient<IStudentWriter , XlsxStudentWriter>();
#endif

            // 注册冲突解决器
            services.AddSingleton<IConflictResolver, DefaultConflictResolver>();

            // 注册场地仓储（全局单例，使用有效数据路径）
            services.AddSingleton<IVenueRepository>(sp => new JsonVenueRepository(store, "Venues",
                sp.GetRequiredService<FileMigrationService>(),
                sp.GetRequiredService<ILogger<JsonVenueRepository>>()));

            // 注册 AppSettings 仓储（始终位于默认数据目录，避免查找自身的鸡生蛋问题）
            if (defaultAppSettingsPath is not null)
            {
                services.AddSingleton<IAppSettingsRepository>(sp => new JsonAppSettingsRepository(defaultAppSettingsPath,
                    sp.GetRequiredService<FileMigrationService>()));
            }
            else
            {
                services.AddSingleton<IAppSettingsRepository>(sp => new JsonAppSettingsRepository(store, "AppSettings.json",
                    sp.GetRequiredService<FileMigrationService>()));
            }

            // 注册学生数据集仓储（全局单例）
            services.AddSingleton<IStudentDatasetRepository>(sp => new JsonStudentDatasetRepository(store, "Rosters",
                sp.GetRequiredService<FileMigrationService>(),
                sp.GetRequiredService<ILogger<JsonStudentDatasetRepository>>()));

            // 注册策略 Manifest 提供器（全局单例）
            services.AddSingleton(sp => new StrategyManifestProvider(
                sp.GetRequiredService<ILogger<StrategyManifestProvider>>()));

            // 注册策略运行时配置仓储（per-file，全局单例）
            services.AddSingleton(sp => new StrategyConfigFileRepository(
                store, "StrategyConfig",
                sp.GetRequiredService<FileMigrationService>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StrategyConfigFileRepository>>()));

            // 注册策略数据集配置仓储（per-strategy sub-directory，全局单例）
            services.AddSingleton(sp => new StrategyDatasetConfigRepository(
                store, "StrategyConfig",
                sp.GetRequiredService<FileMigrationService>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StrategyDatasetConfigRepository>>()));

            // 注册 .seatsets 数据包服务（全局单例。浏览器端备份包导出/导入仍可用：数据来自 store）
            services.AddSingleton<ISeatSetsService>(sp => new SeatSetsService(
                store,
                sp.GetRequiredService<ILogger<SeatSetsService>>()));

            return services;
        }

        private static LogEventLevel ParseLogLevel(string level) => level?.ToLowerInvariant() switch
        {
            "debug" => LogEventLevel.Debug,
            "information" => LogEventLevel.Information,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };

        /// <summary>
        /// 清理旧的日志文件，仅保留最近 <paramref name="retainCount"/> 个。
        /// 文件按创建时间降序排列（最新的在前）。
        /// </summary>
        private static void PruneOldLogFiles(string logDir, int retainCount)
        {
            if (retainCount <= 0) return;
            try
            {
                var files = Directory.GetFiles(logDir, "SeatFlow_*.log")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .ToList();

                for (int i = retainCount; i < files.Count; i++)
                {
                    try { files[i].Delete(); }
                    catch { /* 删除失败忽略 */ }
                }
            }
            catch { /* 枚举失败忽略 */ }
        }
    }
}
