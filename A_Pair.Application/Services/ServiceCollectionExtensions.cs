using System.IO;
using A_Pair.Application.Interfaces;
using A_Pair.Application.Plugins;
using A_Pair.Contracts.Interfaces;
using A_Pair.Core.Exporters;
using A_Pair.Core.Providers;
using A_Pair.Core.Services;
using A_Pair.Core.Strategies;
using A_Pair.Infrastructure.Exporters;
using A_Pair.Infrastructure.Providers;
using A_Pair.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace A_Pair.Application.Services
{
    /// <summary>
    /// 提供 <see cref="IServiceCollection"/> 的扩展方法，用于注册 A_Pair 应用程序层的所有服务。
    /// </summary>
    /// <remarks>
    /// 此扩展方法一次性注册以下组件：
    /// <list type="bullet">
    ///   <item><see cref="IApplicationFacade"/> — 应用程序外观</item>
    ///   <item>内置策略（<see cref="FixedSeatStrategy"/>、<see cref="RandomFillStrategy"/>、<see cref="FrontRowRotationStrategy"/>、<see cref="DeskMateStrategy"/>)</item>
    ///   <item>导出器（Excel、CSV、PDF）</item>
    ///   <item>学生写入器（JSON、CSV、XLSX）</item>
    ///   <item><see cref="IConflictResolver"/> — 冲突解决器</item>
    ///   <item><see cref="PluginManager"/> 与 <see cref="IPluginConfigurationService"/> — 插件管理</item>
    ///   <item><see cref="IVenueRepository"/> 与 <see cref="IAppSettingsRepository"/> — 数据持久化</item>
    ///   <item><see cref="SeatingSnapshotRepository"/> — 快照存储</item>
    /// </list>
    /// </remarks>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 将 A_Pair 应用程序层的所有服务注册到依赖注入容器中。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="snapshotBasePath">快照存储的基路径。</param>
        /// <param name="pluginsPath">插件目录的路径。</param>
        /// <returns>服务集合，支持链式调用。</returns>
        public static IServiceCollection AddA_PairApplication (this IServiceCollection services , string snapshotBasePath , string pluginsPath)
        {
            services.AddLogging();
            services.AddSingleton<CsvStudentProvider>();
            services.AddSingleton<XlsxStudentProvider>();
            services.AddSingleton<JsonStudentProvider>();
            services.TryAddSingleton<IStudentProvider , CompositeStudentProvider>();
            services.AddSingleton<ISeatingSnapshotRepository>(sp =>
                new SeatingSnapshotRepository(Path.Combine(snapshotBasePath, "Assignments")));
            services.AddSingleton<IApplicationFacade , ApplicationFacade>();


            // 注册内置策略
            services.AddSingleton<ISeatingStrategy , FixedSeatStrategy>();
            services.AddSingleton<ISeatingStrategy , RandomFillStrategy>();
            services.AddSingleton<ISeatingStrategy , FrontRowRotationStrategy>();
            services.AddSingleton<ISeatingStrategy , DeskMateStrategy>();

            // 注册导出器
            services.AddSingleton<ISeatingPlanExporter , ExcelSeatingExporter>();
            services.AddSingleton<ISeatingPlanExporter , CsvSeatingExporter>();
            services.AddSingleton<ISeatingPlanExporter , PdfSeatingExporter>();
            services.AddSingleton<ISeatingPlanExporter , ImageSeatingExporter>();
            services.AddTransient<IStudentWriter , JsonStudentWriter>();
            services.AddTransient<IStudentWriter , CsvStudentWriter>();
            services.AddTransient<IStudentWriter , XlsxStudentWriter>();

            // 注册冲突解决器
            services.AddSingleton<IConflictResolver , DefaultConflictResolver>();

            // 注册插件管理器与配置服务
            services.AddSingleton<IPluginManager>(sp =>
                new PluginManager(pluginsPath, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PluginManager>>()));
            services.AddSingleton<IPluginConfigurationService>(sp => new PluginConfigurationService(pluginsPath));

            // 注册场地仓储（全局单例）
            var venuesPath = Path.Combine(snapshotBasePath , "Venues");
            services.AddSingleton<IVenueRepository>(sp => new JsonVenueRepository(venuesPath));

            // 注册 AppSettings 仓储（全局单例）
            var appDataDir = Path.GetDirectoryName(Path.GetFullPath(snapshotBasePath)) ?? snapshotBasePath;
            var settingsPath = Path.Combine(appDataDir, "AppSettings.json");
            services.AddSingleton<IAppSettingsRepository>(sp => new JsonAppSettingsRepository(settingsPath));

            // 注册学生数据集仓储（全局单例）
            var rostersPath = Path.Combine(snapshotBasePath , "Rosters");
            services.AddSingleton<IStudentDatasetRepository>(sp => new JsonStudentDatasetRepository(rostersPath));

            // 注册策略 Manifest 提供器（全局单例）
            services.AddSingleton<StrategyManifestProvider>();

            // 注册策略运行时配置仓储（per-file，全局单例）
            var strategyConfigDir = Path.Combine(appDataDir, "StrategyConfig");
            services.AddSingleton(sp => new StrategyConfigFileRepository(
                strategyConfigDir, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StrategyConfigFileRepository>>()));

            return services;
        }
    }
}
