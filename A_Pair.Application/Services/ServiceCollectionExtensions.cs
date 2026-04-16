using System;
using A_Pair.Application.Interfaces;
using A_Pair.Application.Services;
using A_Pair.Core.Providers;
using A_Pair.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace A_Pair.Application.Services
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddA_PairApplication(this IServiceCollection services, string snapshotBasePath)
        {
            services.AddSingleton<SeatingSnapshotRepository>(sp => new SeatingSnapshotRepository(snapshotBasePath));
            services.AddSingleton<IApplicationFacade, ApplicationFacade>();
            // register default strategies for convenience
            services.AddSingleton<A_Pair.Core.Strategies.ISeatingStrategy, A_Pair.Core.Strategies.FixedSeatStrategy>();
            services.AddSingleton<A_Pair.Core.Strategies.ISeatingStrategy, A_Pair.Core.Strategies.RandomFillStrategy>();
            services.AddSingleton<A_Pair.Core.Strategies.ISeatingStrategy, A_Pair.Core.Strategies.FrontRowRotationStrategy>();
            services.AddSingleton<A_Pair.Core.Strategies.ISeatingStrategy, A_Pair.Core.Strategies.DeskMateStrategy>();
            // exporters
            services.AddSingleton<A_Pair.Core.Exporters.ISeatingPlanExporter, A_Pair.Infrastructure.Exporters.ExcelSeatingExporter>();
            return services;
        }
    }
}
