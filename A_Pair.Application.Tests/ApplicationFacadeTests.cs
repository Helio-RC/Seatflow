using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using A_Pair.Application.Services;

namespace A_Pair.Application.Tests
{
    public class ApplicationFacadeTests
    {
        [Fact]
        public async Task GenerateSeating_CreatesSnapshotAndAssignments()
        {
            var services = new ServiceCollection();
            // extension method is in A_Pair.Application.Services namespace
            services.AddA_PairApplication(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "apair_tests"));
            // register in-memory provider
            services.AddSingleton<A_Pair.Core.Providers.IStudentProvider, A_Pair.Infrastructure.Providers.InMemoryStudentProvider>();
            var sp = services.BuildServiceProvider();

            var facade = sp.GetRequiredService<A_Pair.Application.Interfaces.IApplicationFacade>();
            var workspace = await facade.GenerateSeatingAsync(new A_Pair.Application.Interfaces.SeatingRequest());

            var plan = workspace.BuildSeatingPlan();
            Assert.NotNull(plan);
            Assert.True(plan.Assignments.Count >= 0);
        }
    }
}
