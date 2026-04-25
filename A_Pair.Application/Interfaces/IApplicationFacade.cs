using A_Pair.Core.Models;
using A_Pair.Core.Workspace;

namespace A_Pair.Application.Interfaces
{
    public interface IApplicationFacade
    {
        Task<AppConfiguration> LoadConfigurationAsync (string path , CancellationToken cancellationToken = default);
        Task<List<Student>> LoadStudentsAsync (string source , CancellationToken cancellationToken = default);
        Task<SeatingWorkspace> GenerateSeatingAsync (SeatingRequest request , IProgress<SeatingProgress>? progress = null , CancellationToken cancellationToken = default);
        Task ExportSeatingPlanAsync (SeatingWorkspace workspace , string path , ExportOptions options , CancellationToken cancellationToken = default);
        Task ExportStudentsAsync (string path , IEnumerable<Student> students , ExportFormat format , CancellationToken cancellationToken = default);
        Task<bool> ExecuteCommandAsync (A_Pair.Application.Commands.IUndoableCommand command , CancellationToken cancellationToken = default);
        Task<bool> UndoAsync (CancellationToken cancellationToken = default);
        Task<bool> RedoAsync (CancellationToken cancellationToken = default);
        Task<SeatingWorkspace?> GetCurrentWorkspaceAsync (CancellationToken cancellationToken = default);
        Task<AppSettings> LoadAppSettingsAsync (CancellationToken cancellationToken = default);
        Task SaveAppSettingsAsync (AppSettings settings , CancellationToken cancellationToken = default);
        Task SaveVenueAsync (string venueId , ClassroomLayoutDefinition layout , CancellationToken cancellationToken = default);
        Task<ClassroomLayoutDefinition?> LoadVenueAsync (string venueId , CancellationToken cancellationToken = default);
        Task<IEnumerable<string>> ListVenueIdsAsync (CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SeatingSnapshot>> GetSnapshotsAsync (string venueId , CancellationToken cancellationToken = default);
        Task RollbackToSnapshotAsync (string snapshotId , CancellationToken cancellationToken = default);
    }

    public class AppConfiguration { }

    public class SeatingRequest
    {
        public string? LayoutId { get; set; }

        public LayoutType LayoutType { get; set; }

        public Dictionary<string , object> LayoutParameters { get; set; } = [];
        public List<string> StrategyIds { get; set; } = [];
        public bool UseDefaultStrategies { get; set; } = true;
        public string? StudentDataSource { get; set; }
        public string? Description { get; set; }
    }

    public class SeatingProgress
    {
        public int TotalSteps { get; set; }
        public int CurrentStep { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
    }
}