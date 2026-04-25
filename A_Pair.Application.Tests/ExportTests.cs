using A_Pair.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace A_Pair.Application.Tests
{
    public class ExportTests
    {
        [Fact]
        public async Task ExcelExporter_WritesFile ()
        {
            var services = new ServiceCollection();
            services.AddA_PairApplication(System.IO.Path.Combine(System.IO.Path.GetTempPath() , "apair_tests"));
            services.AddSingleton<A_Pair.Core.Providers.IStudentProvider , A_Pair.Infrastructure.Providers.InMemoryStudentProvider>();
            var sp = services.BuildServiceProvider();

            var exporter = sp.GetRequiredService<A_Pair.Core.Exporters.ISeatingPlanExporter>();
            var plan = new A_Pair.Core.Workspace.SeatingPlan();
            plan.Assignments["s1"] = "student1";
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath() , "seating_test.xlsx");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);

            await exporter.ExportAsync(plan , path);

            Assert.True(System.IO.File.Exists(path));
        }
    }
}
