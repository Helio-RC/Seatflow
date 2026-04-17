using A_Pair.Application.Interfaces;
using A_Pair.Application.Services;
using A_Pair.Core.Exporters;
using A_Pair.Core.Providers;
using A_Pair.Infrastructure.Exporters;
using A_Pair.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace A_Pair.Application.Services
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddA_PairApplication(this IServiceCollection services, string snapshotBasePath)
        {
            services.AddSingleton<ISeatingPlanExporter, ExcelSeatingExporter>();
            services.AddSingleton<ISeatingPlanExporter, CsvSeatingExporter>();
            services.AddSingleton<ISeatingPlanExporter, PdfSeatingExporter>();
            services.AddSingleton<IConflictResolver , DefaultConflictResolver>();
            return services;
        }
    }
}
