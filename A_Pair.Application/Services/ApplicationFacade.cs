using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Commands;
using A_Pair.Application.Interfaces;
using A_Pair.Application.Plugins;
using A_Pair.Application.Services;
using A_Pair.Core.Exporters;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using A_Pair.Core.Strategies;
using A_Pair.Core.Workspace;
using A_Pair.Infrastructure.Exporters;
using A_Pair.Infrastructure.Layouts;
using A_Pair.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace A_Pair.Application.Services
{
    public class ApplicationFacade : IApplicationFacade
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly SeatingSnapshotRepository _snapshotRepository;
        private readonly IEnumerable<ISeatingPlanExporter> _exporters;
        private readonly PluginManager _pluginManager;
        private readonly IPluginConfigurationService _pluginConfigService;
        private readonly CommandHistory _history = new();
        private SeatingWorkspace? _currentWorkspace;

        public ApplicationFacade (
            IServiceProvider serviceProvider ,
            SeatingSnapshotRepository snapshotRepository ,
            IEnumerable<ISeatingPlanExporter> exporters ,
            PluginManager pluginManager ,
            IPluginConfigurationService pluginConfigService)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
            _exporters = exporters ?? throw new ArgumentNullException(nameof(exporters));
            _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
            _pluginConfigService = pluginConfigService ?? throw new ArgumentNullException(nameof(pluginConfigService));
        }

        public Task<AppConfiguration> LoadConfigurationAsync (string path , CancellationToken cancellationToken = default)
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
            // 1. 加载学生数据
            var studentProvider = _serviceProvider.GetService<IStudentProvider>();
            var students = studentProvider == null
                ? new List<Student>()
                : await studentProvider.LoadAsync(request.StudentDataSource ?? string.Empty , cancellationToken);

            // 2. 根据请求生成座位布局
            List<Seat> seats;
            if (!string.IsNullOrEmpty(request.LayoutId))
            {
                // TODO: 从存储中加载已保存的布局
                seats = new List<Seat>();
            }
            else
            {
                seats = BuildSeatsFromRequest(request);
            }

            // 3. 创建工作区
            var workspace = new SeatingWorkspace(students , seats);
            _currentWorkspace = workspace;

            // 4. 获取内置策略
            var strategies = _serviceProvider.GetServices<ISeatingStrategy>().ToList();

            // 5. 加载插件策略并适配
            var loadedPlugins = _pluginManager.LoadPlugins();
            foreach (var pluginInfo in loadedPlugins)
            {
                if (pluginInfo.Strategy.IsEnabled)
                {
                    var adapter = new PluginStrategyAdapter(pluginInfo.Strategy);
                    strategies.Add(adapter);
                }
            }

            // 6. 按请求过滤策略
            if (!request.UseDefaultStrategies && request.StrategyIds.Any())
            {
                strategies = strategies.Where(s => request.StrategyIds.Contains(s.Id)).ToList();
            }

            // 7. 执行策略管道
            var pipeline = new StrategyExecutionPipeline(strategies);
            var plan = await pipeline.ExecuteAsync(workspace , progress , cancellationToken);

            // 8. 解决冲突
            var conflictResolver = _serviceProvider.GetService<IConflictResolver>();
            if (conflictResolver != null)
            {
                var conflictResult = conflictResolver.Resolve(workspace);
                if (!conflictResult.Success)
                {
                    progress?.Report(new SeatingProgress
                    {
                        CurrentStep = 1 ,
                        TotalSteps = 1 ,
                        StatusMessage = $"检测到 {conflictResult.Conflicts.Count} 个冲突，已自动处理"
                    });
                }
            }

            // 9. 保存快照
            var snapshot = new SeatingSnapshot
            {
                Description = request.Description ?? $"生成于 {DateTime.Now:yyyy-MM-dd HH:mm}" ,
                SeatAssignments = plan.Assignments
            };
            await _snapshotRepository.SaveAsync(snapshot);

            progress?.Report(new SeatingProgress
            {
                CurrentStep = 1 ,
                TotalSteps = 1 ,
                StatusMessage = "座位生成完成"
            });

            return workspace;
        }

        public async Task ExportSeatingPlanAsync (
            SeatingWorkspace workspace ,
            string path ,
            ExportOptions options ,
            CancellationToken cancellationToken = default)
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
            await exporter.ExportAsync(plan , path , cancellationToken);
        }

        public async Task<bool> ExecuteCommandAsync (IUndoableCommand command , CancellationToken cancellationToken = default)
        {
            if (_currentWorkspace == null)
            {
                await GenerateSeatingAsync(new SeatingRequest() , null , cancellationToken);
            }

            if (_currentWorkspace == null) return false;

            return await _history.ExecuteAsync(command , _currentWorkspace , cancellationToken);
        }

        public Task<bool> UndoAsync (CancellationToken cancellationToken = default)
        {
            if (_currentWorkspace == null) return Task.FromResult(false);
            return _history.UndoAsync(_currentWorkspace , cancellationToken);
        }

        public Task<bool> RedoAsync (CancellationToken cancellationToken = default)
        {
            if (_currentWorkspace == null) return Task.FromResult(false);
            return _history.RedoAsync(_currentWorkspace , cancellationToken);
        }

        public Task<SeatingWorkspace?> GetCurrentWorkspaceAsync (CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_currentWorkspace);
        }

        public Task<IReadOnlyList<SeatingSnapshot>> GetSnapshotsAsync (string venueId , CancellationToken cancellationToken = default)
        {
            // TODO: 实现快照查询
            return Task.FromResult<IReadOnlyList<SeatingSnapshot>>(new List<SeatingSnapshot>());
        }

        public Task RollbackToSnapshotAsync (string snapshotId , CancellationToken cancellationToken = default)
        {
            // TODO: 实现回滚逻辑
            return Task.CompletedTask;
        }

        #region Private Helpers

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
            int rows = GetParameter(parameters , "Rows" , 3);
            int columns = GetParameter(parameters , "Columns" , 3);
            var layout = GridLayoutBuilder.BuildGrid(rows , columns);
            return layout.Seats;
        }

        private List<Seat> BuildPolarSeats (Dictionary<string , object> parameters)
        {
            double radiusStep = GetParameter(parameters , "RadiusStep" , 1.0);
            int rings = GetParameter(parameters , "Rings" , 2);
            int seatsPerRing = GetParameter(parameters , "SeatsPerRing" , 8);
            var layout = PolarLayoutBuilder.BuildPolar(radiusStep , rings , seatsPerRing);
            return layout.Seats;
        }

        private List<Seat> BuildFreeformSeats (Dictionary<string , object> parameters)
        {
            // 自由点布局需传入点列表，此处简化返回空列表
            return new List<Seat>();
        }

        private T GetParameter<T> (Dictionary<string , object> parameters , string key , T defaultValue)
        {
            if (parameters.TryGetValue(key , out var value))
            {
                try
                {
                    if (value is T typedValue)
                        return typedValue;
                    return (T)Convert.ChangeType(value , typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        #endregion
    }
}