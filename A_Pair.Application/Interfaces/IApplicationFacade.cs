using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Workspace;
using A_Pair.Core.Models;

namespace A_Pair.Application.Interfaces
{
    public interface IApplicationFacade
    {
        Task<AppConfiguration> LoadConfigurationAsync(string path, CancellationToken cancellationToken = default);
        Task<List<Student>> LoadStudentsAsync(string source, CancellationToken cancellationToken = default);
        Task<SeatingWorkspace> GenerateSeatingAsync(SeatingRequest request, IProgress<SeatingProgress>? progress = null, CancellationToken cancellationToken = default);
        Task ExportSeatingPlanAsync(SeatingWorkspace plan, string path, CancellationToken cancellationToken = default);
    }
    public class AppConfiguration { }
    public class SeatingRequest { }
    public class SeatingProgress { }
}
