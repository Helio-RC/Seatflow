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
        Task<SeatingWorkspace> GenerateSeatingAsync (SeatingRequest request , IProgress<SeatingProgress>? progress = null , CancellationToken cancellationToken = default);
        Task ExportSeatingPlanAsync(SeatingWorkspace workspace, string path, ExportOptions options, CancellationToken cancellationToken = default);
        Task<bool> ExecuteCommandAsync(A_Pair.Application.Commands.IUndoableCommand command, CancellationToken cancellationToken = default);
        Task<bool> UndoAsync(CancellationToken cancellationToken = default);
        Task<bool> RedoAsync(CancellationToken cancellationToken = default);
        Task<SeatingWorkspace?> GetCurrentWorkspaceAsync(CancellationToken cancellationToken = default);
    }

    public class AppConfiguration { }

    public class SeatingRequest
    {
        public string? LayoutId { get; set; }
        public LayoutType LayoutType { get; set; }
        public Dictionary<string , object> LayoutParameters { get; set; } = new();
        public List<string> StrategyIds { get; set; } = new();
        public bool UseDefaultStrategies { get; set; } = true;
        public string? StudentDataSource { get; set; }          // 新增：学生数据源路径
        public string? Description { get; set; }                // 新增：本次生成的描述
    }

    public class SeatingProgress
    {
        public int TotalSteps { get; set; }
        public int CurrentStep { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
    }

    public class ExportOptions
    {
        public ExportFormat Format { get; set; } = ExportFormat.Excel;
        public bool Anonymize { get; set; }
        public bool IncludeMetadata { get; set; }
        public Dictionary<string, object> AdditionalSettings { get; set; } = new();
    }

    public enum ExportFormat
    {
        Excel,
        Csv,
        Pdf
    }
}
