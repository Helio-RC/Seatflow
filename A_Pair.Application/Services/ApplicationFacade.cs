using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Models;
using A_Pair.Infrastructure.Layouts;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Exporters;
using A_Pair.Core.Providers;
using A_Pair.Core.Strategies;
using A_Pair.Core.Workspace;
using A_Pair.Infrastructure.Exporters;
using A_Pair.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace A_Pair.Application.Services
{
    public class ApplicationFacade : IApplicationFacade
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly SeatingSnapshotRepository _snapshotRepository;
        private readonly CommandHistory _history = new();
        private SeatingWorkspace? _currentWorkspace;

        private readonly IEnumerable<ISeatingPlanExporter> _exporters;

        public ApplicationFacade(
            IServiceProvider serviceProvider,
            SeatingSnapshotRepository snapshotRepository,
            IEnumerable<ISeatingPlanExporter> exporters)
        {
            _serviceProvider = serviceProvider;
            _snapshotRepository = snapshotRepository;
            _exporters = exporters;
        }
        public ApplicationFacade(IServiceProvider serviceProvider, SeatingSnapshotRepository snapshotRepository)
        {
            _serviceProvider = serviceProvider;
            _snapshotRepository = snapshotRepository;
        }

        public Task<AppConfiguration> LoadConfigurationAsync(string path, CancellationToken cancellationToken = default)
        {
            // Minimal placeholder: read json if exists
            return Task.FromResult(new AppConfiguration());
        }

        public async Task<List<Student>> LoadStudentsAsync (string source , CancellationToken cancellationToken = default)
        {
            var provider = _serviceProvider.GetService<IStudentProvider>();
            if (provider == null)
                return new List<Student>();

            return await provider.LoadAsync(source , cancellationToken);
        }

        public async Task<SeatingWorkspace> GenerateSeatingAsync (
    SeatingRequest request ,
    IProgress<SeatingProgress>? progress = null ,
    CancellationToken cancellationToken = default)
        {
            // 1. 加载学生数据（此处简化，实际应从请求中的数据源加载）
            var studentProvider = _serviceProvider.GetService<IStudentProvider>();
            var students = studentProvider == null
                ? new List<Student>()
                : await studentProvider.LoadAsync(request.StudentDataSource ?? string.Empty , cancellationToken);

            // 2. 根据请求生成座位布局
            List<Seat> seats;
            if (!string.IsNullOrEmpty(request.LayoutId))
            {
                // 从存储中加载已保存的布局（此处简化，实际应通过 ILayoutRepository）
                seats = new List<Seat>();
            }
            else
            {
                seats = BuildSeatsFromRequest(request);
            }

            // 3. 创建工作区
            var workspace = new SeatingWorkspace(students , seats);
            _currentWorkspace = workspace;

            // 4. 获取策略集合并执行管道
            var strategies = _serviceProvider.GetServices<ISeatingStrategy>().ToList();
            if (!request.UseDefaultStrategies)
            {
                strategies = strategies.Where(s => request.StrategyIds.Contains(s.Id)).ToList();
            }

            var pipeline = new StrategyExecutionPipeline(strategies);
            var plan = await pipeline.ExecuteAsync(workspace , progress , cancellationToken);

            // 5. 报告进度（简单实现）
            progress?.Report(new SeatingProgress
            {
                CurrentStep = 1 ,
                TotalSteps = 1 ,
                StatusMessage = "座位生成完成"
            });

            // 6. 保存快照
            var snapshot = new SeatingSnapshot
            {
                Description = request.Description ?? $"生成于 {DateTime.Now}" ,
                SeatAssignments = plan.Assignments
            };
            await _snapshotRepository.SaveAsync(snapshot);

            return workspace;
        }

        // 辅助方法：根据请求参数构建座位
        private List<Seat> BuildSeatsFromRequest (SeatingRequest request)
        {
            return request.LayoutType switch
            {
                LayoutType.Grid => BuildGridSeats(request.LayoutParameters),
                LayoutType.Polar => BuildPolarSeats(request.LayoutParameters),
                LayoutType.Freeform => BuildFreeformSeats(request.LayoutParameters),
                _ => BuildGridSeats(new Dictionary<string , object> { ["Rows"] = 3 , ["Columns"] = 3 })
            };
        }

        private List<Seat> BuildGridSeats (Dictionary<string , object> parameters)
        {
            int rows = GetParameter<int>(parameters , "Rows" , 3);
            int columns = GetParameter<int>(parameters , "Columns" , 3);
            var layout = GridLayoutBuilder.BuildGrid(rows , columns);
            return layout.Seats;
        }

        private List<Seat> BuildPolarSeats (Dictionary<string , object> parameters)
        {
            double radiusStep = GetParameter<double>(parameters , "RadiusStep" , 1.0);
            int rings = GetParameter<int>(parameters , "Rings" , 2);
            int seatsPerRing = GetParameter<int>(parameters , "SeatsPerRing" , 8);
            var layout = PolarLayoutBuilder.BuildPolar(radiusStep , rings , seatsPerRing);
            return layout.Seats;
        }

        private List<Seat> BuildFreeformSeats (Dictionary<string , object> parameters)
        {
            // 自由点布局需传入点列表，此处简化
            return new List<Seat>();
        }

        private T GetParameter<T> (Dictionary<string , object> parameters , string key , T defaultValue)
        {
            if (parameters.TryGetValue(key , out var value) && value is T typedValue)
                return typedValue;
            return defaultValue;
        }
        public async Task<bool> ExecuteCommandAsync(A_Pair.Application.Commands.IUndoableCommand command, CancellationToken cancellationToken = default)
        {
            // Ensure we have a current workspace; generate a default one if needed
            if (_currentWorkspace == null)
            {
                await GenerateSeatingAsync(new SeatingRequest(), null, cancellationToken);
            }

            if (_currentWorkspace == null) return false;

            return await _history.ExecuteAsync(command, _currentWorkspace, cancellationToken);
        }

        public Task<bool> UndoAsync(CancellationToken cancellationToken = default)
        {
            if (_currentWorkspace == null) return Task.FromResult(false);
            return _history.UndoAsync(_currentWorkspace, cancellationToken);
        }

        public Task<bool> RedoAsync(CancellationToken cancellationToken = default)
        {
            if (_currentWorkspace == null) return Task.FromResult(false);
            return _history.RedoAsync(_currentWorkspace, cancellationToken);
        }

        public Task<SeatingWorkspace?> GetCurrentWorkspaceAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_currentWorkspace);
        }

        public async Task ExportSeatingPlanAsync(SeatingWorkspace workspace, string path, ExportOptions options, CancellationToken cancellationToken = default)
        {
            var plan = workspace.BuildSeatingPlan();

            ISeatingPlanExporter? exporter = options.Format switch
            {
                ExportFormat.Excel => _exporters.OfType<ExcelSeatingExporter>().FirstOrDefault(),
                ExportFormat.Csv => _exporters.OfType<CsvSeatingExporter>().FirstOrDefault(),
                ExportFormat.Pdf => _exporters.OfType<PdfSeatingExporter>().FirstOrDefault(),
                _ => throw new NotSupportedException($"Unsupported export format: {options.Format}")
            };

            if (exporter == null)
                throw new InvalidOperationException($"No exporter found for format {options.Format}.");

            // TODO: 实现 Anonymize 和 IncludeMetadata 逻辑
            await exporter.ExportAsync(plan, path, cancellationToken);
        }
    }
}
