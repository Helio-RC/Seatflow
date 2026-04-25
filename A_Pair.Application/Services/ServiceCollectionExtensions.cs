using A_Pair.Application.Interfaces;
using A_Pair.Application.Plugins;
using A_Pair.Core.Exporters;
using A_Pair.Core.Providers;
using A_Pair.Core.Strategies;
using A_Pair.Infrastructure.Exporters;
using A_Pair.Infrastructure.Providers;
using A_Pair.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace A_Pair.Application.Services
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddA_PairApplication (this IServiceCollection services , string snapshotBasePath , string pluginsPath)
        {
            services.AddSingleton<SeatingSnapshotRepository>(sp => new SeatingSnapshotRepository(snapshotBasePath));
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
            services.AddTransient<IStudentWriter , JsonStudentWriter>();
            services.AddTransient<IStudentWriter , CsvStudentWriter>();
            services.AddTransient<IStudentWriter , XlsxStudentWriter>();

            // 注册冲突解决器
            services.AddSingleton<IConflictResolver , DefaultConflictResolver>();

            // 注册插件管理器与配置服务
            services.AddSingleton<PluginManager>(sp => new PluginManager(pluginsPath));
            services.AddSingleton<IPluginConfigurationService>(sp => new PluginConfigurationService(pluginsPath));

            // 注册场地仓储（全局单例）
            var venuesPath = Path.Combine(snapshotBasePath , "Venues");
            services.AddSingleton<IVenueRepository>(sp => new JsonVenueRepository(venuesPath));

            // 注册 AppSettings 仓储（全局单例）
            var settingsPath = Path.Combine(snapshotBasePath , ".." , "AppSettings.json");
            services.AddSingleton<IAppSettingsRepository>(sp => new JsonAppSettingsRepository(settingsPath));
            return services;
        }
    }
}
