using A_Pair.Application.Commands;
using A_Pair.Application.Interfaces;
using A_Pair.Application.Plugins;
using A_Pair.Contracts.Interfaces;
using A_Pair.Core.DomainServices;
using A_Pair.Core.Exporters;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using A_Pair.Core.Services;
using A_Pair.Core.Strategies;
using A_Pair.Core.Workspace;
using A_Pair.Infrastructure.Layouts;
using A_Pair.Infrastructure.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace A_Pair.Application.Services
{
    /// <summary>
    /// 应用程序外观（Facade）的默认实现，协调数据加载、布局生成、策略执行、
    /// 冲突解决、快照管理等核心业务流程，为 UI/CLI 层提供统一的高层 API。
    /// </summary>
    /// <remarks>
    /// <see cref="ApplicationFacade"/> 封装了完整的座位编排工作流：
    /// <list type="number">
    ///   <item>加载学生数据（通过 <see cref="IStudentProvider"/>）</item>
    ///   <item>生成或加载座位布局（Grid / Polar / Freeform）</item>
    ///   <item>创建 <see cref="SeatingWorkspace"/> 工作区</item>
    ///   <item>获取内置策略与插件策略，按优先级排序后依次执行</item>
    ///   <item>通过 <see cref="IConflictResolver"/> 检测并自动修复冲突</item>
    ///   <item>保存 <see cref="SeatingSnapshot"/> 快照以支持回滚</item>
    /// </list>
    /// </remarks>
    public class ApplicationFacade (
        IServiceProvider serviceProvider ,
        ISeatingSnapshotRepository snapshotRepository ,
        IEnumerable<ISeatingPlanExporter> exporters ,
        IPluginManager pluginManager ,
        IPluginConfigurationService pluginConfigService ,
        IAppSettingsRepository appSettingsRepo ,
        IVenueRepository venueRepo ,
        IStudentDatasetRepository datasetRepo ,
        StrategyManifestProvider manifestProvider ,
        StrategyConfigFileRepository strategyConfigRepo ,
        ILogger<ApplicationFacade> logger) : IApplicationFacade
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        private readonly ISeatingSnapshotRepository _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
        private readonly IEnumerable<ISeatingPlanExporter> _exporters = exporters ?? throw new ArgumentNullException(nameof(exporters));
        private readonly IPluginManager _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        private readonly IPluginConfigurationService _pluginConfigService = pluginConfigService ?? throw new ArgumentNullException(nameof(pluginConfigService));
        private readonly CommandHistory _history = new();
        private readonly IAppSettingsRepository _appSettingsRepo = appSettingsRepo ?? throw new ArgumentNullException(nameof(appSettingsRepo));
        private readonly IVenueRepository _venueRepo = venueRepo ?? throw new ArgumentNullException(nameof(venueRepo));
        private readonly IStudentDatasetRepository _datasetRepo = datasetRepo ?? throw new ArgumentNullException(nameof(datasetRepo));
        private readonly StrategyManifestProvider _manifestProvider = manifestProvider ?? throw new ArgumentNullException(nameof(manifestProvider));
        private readonly StrategyConfigFileRepository _strategyConfigRepo = strategyConfigRepo ?? throw new ArgumentNullException(nameof(strategyConfigRepo));
        private SeatingWorkspace? _currentWorkspace;
        private ClassroomLayoutDefinition? _currentLayout;
        private List<ISeatingStrategy>? _cachedStrategies; // 策略为 Singleton，缓存避免重复物化

        /// <inheritdoc />
        public Task<AppConfiguration> LoadConfigurationAsync (string path , CancellationToken cancellationToken = default)
            => Task.FromResult(new AppConfiguration());

        /// <inheritdoc />
        public Task<AppSettings> LoadAppSettingsAsync (CancellationToken cancellationToken = default)
            => _appSettingsRepo.LoadAsync(cancellationToken);

        /// <inheritdoc />
        public Task SaveAppSettingsAsync (AppSettings settings , CancellationToken cancellationToken = default)
            => _appSettingsRepo.SaveAsync(settings , cancellationToken);

        /// <inheritdoc />
        public Task SaveVenueAsync (string venueId , ClassroomLayoutDefinition layout , CancellationToken cancellationToken = default)
            => _venueRepo.SaveAsync(venueId , layout , cancellationToken);

        /// <inheritdoc />
        public Task<ClassroomLayoutDefinition?> LoadVenueAsync (string venueId , CancellationToken cancellationToken = default)
            => _venueRepo.LoadAsync(venueId , cancellationToken);

        /// <inheritdoc />
        public Task<IEnumerable<string>> ListVenueIdsAsync (CancellationToken cancellationToken = default)
            => _venueRepo.ListVenueIdsAsync(cancellationToken);

        /// <inheritdoc />
        public Task DeleteVenueAsync (string venueId , CancellationToken cancellationToken = default)
            => _venueRepo.DeleteAsync(venueId , cancellationToken);

        /// <inheritdoc />
        public async Task<List<Student>> LoadStudentsAsync (string source , CancellationToken cancellationToken = default)
        {
            var provider = _serviceProvider.GetService<IStudentProvider>();
            if (provider == null) return new List<Student>();
            return await provider.LoadAsync(source , cancellationToken);
        }

        /// <inheritdoc />
        public async Task ExportStudentsAsync (string path , IEnumerable<Student> students , ExportFormat format , CancellationToken cancellationToken = default)
        {
            var writers = _serviceProvider.GetServices<IStudentWriter>();
            IStudentWriter writer = format switch
            {
                ExportFormat.Excel => writers.OfType<XlsxStudentWriter>().FirstOrDefault()
                    ?? throw new InvalidOperationException("未注册 XlsxStudentWriter"),
                ExportFormat.Csv => writers.OfType<CsvStudentWriter>().FirstOrDefault()
                    ?? throw new InvalidOperationException("未注册 CsvStudentWriter"),
                ExportFormat.Json => writers.OfType<JsonStudentWriter>().FirstOrDefault()
                    ?? throw new InvalidOperationException("未注册 JsonStudentWriter"),
                _ => throw new NotSupportedException($"不支持的导出格式: {format}")
            };
            await writer.WriteAsync(path , students , cancellationToken);
        }

        /// <inheritdoc />
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

            // 2. 生成座位布局
            List<Seat> seats;
            ClassroomLayoutDefinition? venueLayout = null;
            if (!string.IsNullOrEmpty(request.LayoutId))
            {
                // 从已保存的会场加载布局
                venueLayout = await _venueRepo.LoadAsync(request.LayoutId , cancellationToken);
                _currentLayout = venueLayout;
                if (venueLayout != null)
                {
                    seats = venueLayout.Seats;
                    // 应用障碍物 (已在保存时处理，但为确保安全再次处理)
                    ObstacleProcessor.ApplyObstacles(venueLayout);
                }
                else
                {
                    seats = new List<Seat>();
                }
            }
            else
            {
                seats = BuildSeatsFromRequest(request);
            }

            // 3. 创建工作区
            var workspace = new SeatingWorkspace(students , seats);
            _currentWorkspace = workspace;

            // 4. 获取内置策略
            var strategies = _cachedStrategies ??= _serviceProvider.GetServices<ISeatingStrategy>().ToList();

            // 5. 加载插件策略并适配
            var loadedPlugins = await _pluginManager.LoadStrategyPluginsAsync(cancellationToken);
            foreach (var pluginInfo in loadedPlugins)
            {
                if (pluginInfo.Strategy.IsEnabled)
                {
                    var adapter = new PluginStrategyAdapter(pluginInfo.Strategy);
                    strategies.Add(adapter);
                }
            }

            // 6. 按请求过滤策略
            if (!request.UseDefaultStrategies && request.StrategyIds.Count != 0)
                strategies = strategies.Where(s => request.StrategyIds.Contains(s.Id)).ToList();

            // 6b. 同步 FrontRowCount 到策略配置
            if (venueLayout?.Metadata is GridLayoutMetadata gridMeta)
            {
                var frontRowStrategy = strategies.OfType<FrontRowRotationStrategy>().FirstOrDefault();
                frontRowStrategy?.SetFrontRowCount(gridMeta.FrontRowCount);
            }
            else if (venueLayout?.Metadata is PolarLayoutMetadata polarMeta)
            {
                var frontRowStrategy = strategies.OfType<FrontRowRotationStrategy>().FirstOrDefault();
                frontRowStrategy?.SetFrontRowCount(polarMeta.FrontRowCount);
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
            var studentNames = workspace.Students
                .Where(s => plan.Assignments.Values.Contains(s.Id))
                .ToDictionary(s => s.Id , s => s.Name);
            var snapshot = new SeatingSnapshot
            {
                Description = request.Description ?? $"生成于 {DateTime.Now:yyyy-MM-dd HH:mm}" ,
                LayoutId = request.LayoutId ?? "unknown" ,
                SeatAssignments = plan.Assignments ,
                Metadata = new Dictionary<string , object> { ["studentNames"] = studentNames }
            };
            await _snapshotRepository.SaveAsync(snapshot , cancellationToken);

            // 9b. 保存会场摘要到快照目录
            if (venueLayout != null && !string.IsNullOrEmpty(request.LayoutId))
            {
                await _snapshotRepository.SaveVenueInfoAsync(request.LayoutId , new VenueSnapshotInfo
                {
                    Name = venueLayout.Name ,
                    LayoutType = venueLayout.LayoutType ,
                    SeatCount = venueLayout.Seats.Count(s => s.IsAvailable) ,
                    ObstacleCount = venueLayout.Obstacles.Count
                });
            }

            progress?.Report(new SeatingProgress
            {
                CurrentStep = 1 ,
                TotalSteps = 1 ,
                StatusMessage = "座位生成完成"
            });

            return workspace;
        }

        /// <inheritdoc />
        public async Task ExportSeatingPlanAsync (
    SeatingWorkspace workspace ,
    ClassroomLayoutDefinition? layout ,
    string path ,
    ExportOptions options ,
    CancellationToken cancellationToken = default)
        {
            ISeatingPlanExporter? exporter = _exporters.FirstOrDefault(e => e.Format == options.Format);
            if (exporter == null)
                throw new NotSupportedException($"No exporter registered for format {options.Format}.");

            if (layout != null)
            {
                var assignments = workspace.BuildSeatingPlan().Assignments;
                var studentNames = workspace.Students.ToDictionary(s => s.Id , s => s.Name);
                var model = LayoutSeatingExportModel.FromLayout(layout , assignments , studentNames);
                await exporter.ExportLayoutAsync(model , path , options , cancellationToken);
            }
            else
            {
                var plan = workspace.BuildSeatingPlan();
                await exporter.ExportAsync(plan , path , options , cancellationToken);
            }
        }

        /// <inheritdoc />
        public async Task<bool> ExecuteCommandAsync (IUndoableCommand command , CancellationToken cancellationToken = default)
        {
            if (_currentWorkspace == null) return false;
            return await _history.ExecuteAsync(command , _currentWorkspace , cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> UndoAsync (CancellationToken cancellationToken = default)
        {
            if (_currentWorkspace == null) return Task.FromResult(false);
            return _history.UndoAsync(_currentWorkspace , cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> RedoAsync (CancellationToken cancellationToken = default)
        {
            if (_currentWorkspace == null) return Task.FromResult(false);
            return _history.RedoAsync(_currentWorkspace , cancellationToken);
        }

        /// <inheritdoc />
        public Task<SeatingWorkspace?> GetCurrentWorkspaceAsync (CancellationToken cancellationToken = default)
            => Task.FromResult(_currentWorkspace);

        /// <inheritdoc />
        public Task<ClassroomLayoutDefinition?> GetCurrentLayoutAsync (CancellationToken cancellationToken = default)
            => Task.FromResult(_currentLayout);

        /// <inheritdoc />
        public void ClearWorkspace ()
        {
            _currentWorkspace = null;
            _currentLayout = null;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<SeatingSnapshot>> GetSnapshotsAsync (string venueId , CancellationToken cancellationToken = default)
        {
            // 从存储库中按 venueId 过滤快照
            return await _snapshotRepository.ListByVenueAsync(venueId , cancellationToken);
        }

        /// <inheritdoc />
        public async Task DeleteSnapshotAsync (string snapshotId , CancellationToken cancellationToken = default)
            => await _snapshotRepository.DeleteAsync(snapshotId , cancellationToken);

        /// <inheritdoc />
        public bool HasActiveWorkspace => _currentWorkspace != null;

        /// <inheritdoc />
        public async Task<SeatingSnapshot?> CreateSnapshotAsync (string description , CancellationToken cancellationToken = default)
        {
            if (_currentWorkspace == null) return null;

            var plan = _currentWorkspace.BuildSeatingPlan();
            var studentNames = _currentWorkspace.Students
                .Where(s => plan.Assignments.Values.Contains(s.Id))
                .ToDictionary(s => s.Id , s => s.Name);
            var snapshot = new SeatingSnapshot
            {
                Description = description ,
                LayoutId = plan.Assignments.Count > 0 ? "current" : "empty" ,
                SeatAssignments = plan.Assignments ,
                Metadata = new Dictionary<string , object> { ["studentNames"] = studentNames }
            };
            await _snapshotRepository.SaveAsync(snapshot , cancellationToken);
            return snapshot;
        }

        /// <inheritdoc />
        public async Task RollbackToSnapshotAsync (string snapshotId , CancellationToken cancellationToken = default)
        {
            var snapshot = await _snapshotRepository.LoadAsync(snapshotId , cancellationToken) ?? throw new InvalidOperationException($"快照 {snapshotId} 不存在");

            // 回滚前自动保存当前状态为备份快照，确保可撤销
            if (_currentWorkspace != null)
            {
                try { await CreateSnapshotAsync($"回滚前的自动备份 - {DateTime.Now:yyyy-MM-dd HH:mm}"); } catch { }
            }

            // 加载快照对应的会场布局（无论是否已有工作区都重新加载，确保座位 ID 匹配）
            ClassroomLayoutDefinition? layout = null;
            if (!string.IsNullOrEmpty(snapshot.LayoutId)
                && snapshot.LayoutId != "unknown"
                && snapshot.LayoutId != "empty"
                && snapshot.LayoutId != "current")
            {
                try { layout = await venueRepo.LoadAsync(snapshot.LayoutId , cancellationToken); } catch { }
            }

            _currentLayout = layout;

            var seats = layout?.Seats ?? new List<Seat>();
            var studentIds = snapshot.SeatAssignments.Values
                .Where(v => v != null)
                .Distinct()
                .ToList();
            var students = await BuildStudentsForSnapshotAsync(studentIds , cancellationToken);

            _currentWorkspace = new SeatingWorkspace(students , seats);
            _currentWorkspace.ApplySnapshotAssignments(snapshot.SeatAssignments);
        }

        /// <summary>
        /// 从已保存的学生数据集中尽可能匹配真实学生信息，无法匹配的用 ID 作为名称的存根学生。
        /// </summary>
        private async Task<List<Student>> BuildStudentsForSnapshotAsync (List<string> studentIds , CancellationToken ct)
        {
            if (studentIds.Count == 0)
                return [];

            var idSet = new HashSet<string>(studentIds);
            var studentMap = new Dictionary<string , Student>();

            // 尝试从所有已保存的学生数据集加载真实数据
            try
            {
                var datasets = await _datasetRepo.ListAsync(ct);
                foreach (var ds in datasets)
                {
                    var loaded = await _datasetRepo.LoadAsync(ds.Id , ct);
                    if (loaded != null)
                    {
                        foreach (var s in loaded)
                        {
                            if (idSet.Contains(s.Id) && !studentMap.ContainsKey(s.Id))
                                studentMap[s.Id] = s;
                        }
                    }
                }
            }
            catch { /* 数据集读取失败不影响回滚 */ }

            return studentIds.Select(id =>
                studentMap.TryGetValue(id , out var real) ? real : new Student { Id = id , Name = id }
            ).ToList();
        }

        public async Task<string> SaveStudentDatasetAsync (string name , List<Student> students , string? originalFileName = null , CancellationToken ct = default)
        {
            var id = Guid.NewGuid().ToString("N");
            await _datasetRepo.SaveAsync(id , name , students , originalFileName , ct);
            return id;
        }

        public Task<List<Student>?> LoadStudentDatasetAsync (string id , CancellationToken ct = default)
            => _datasetRepo.LoadAsync(id , ct);

        public Task<IReadOnlyList<StudentDatasetInfo>> ListStudentDatasetsAsync (CancellationToken ct = default)
            => _datasetRepo.ListAsync(ct);

        public Task DeleteStudentDatasetAsync (string id , CancellationToken ct = default)
            => _datasetRepo.DeleteAsync(id , ct);

        /// <inheritdoc />
        public Task RenameStudentDatasetAsync (string id , string newName , CancellationToken ct = default)
            => _datasetRepo.RenameAsync(id , newName , ct);

        /// <inheritdoc />
        public async Task<List<StrategyDisplayInfo>> GetStrategiesAsync (CancellationToken ct = default)
        {
            var persisted = await _strategyConfigRepo.LoadAllAsync(ct);
            var result = new List<StrategyDisplayInfo>();

            // 收集内置策略（Manifest + 运行时实例配置）
            var builtInManifests = _manifestProvider.GetBuiltInManifests();
            var builtInInstances = _cachedStrategies ??= _serviceProvider.GetServices<ISeatingStrategy>().ToList();
            foreach (var manifest in builtInManifests)
            {
                var runtimeStrategy = builtInInstances.FirstOrDefault(s => s.Id == manifest.Id);
                var info = BuildDisplayInfo(manifest , "builtin" , persisted , runtimeStrategy);
                result.Add(info);
            }

            // 收集插件策略（PluginManifest → StrategyManifest + 运行时配置）
            var loadedPlugins = await _pluginManager.LoadStrategyPluginsAsync(ct);
            foreach (var pi in loadedPlugins)
            {
                var pluginManifest = new StrategyManifest
                {
                    Id = pi.Manifest.Id ,
                    Name = pi.Manifest.Name ,
                    DisplayName = pi.Manifest.Name ,
                    Version = pi.Manifest.Version ,
                    Description = pi.Manifest.Description ,
                    Author = pi.Manifest.Author ,
                    Category = "plugin" ,
                    DefaultPriority = pi.Manifest.Priority ,
                    DefaultEnabled = pi.Manifest.Enabled
                };

                var runtimePluginConfig = pi.Strategy;
                var source = $"plugin:{pi.Manifest.Id}";
                var info = BuildDisplayInfo(pluginManifest , source , persisted , null);
                result.Add(info);
            }

            var sorted = result.OrderBy(d => d.Priority).ToList();
            logger.LogInformation("加载策略列表：内置 {BuiltIn} 个，插件 {Plugin} 个，共 {Total} 个" ,
                builtInManifests.Count , loadedPlugins.Count() , sorted.Count);
            return sorted;
        }

        /// <inheritdoc />
        public async Task SaveStrategyConfigAsync (string strategyId , StrategyConfig config , CancellationToken ct = default)
        {
            logger.LogInformation("保存策略配置：{Id}，优先级 {Priority}，启用 {Enabled}" ,
                strategyId , config.Priority , config.IsEnabled);

            // 持久化到文件
            await _strategyConfigRepo.SaveAsync(strategyId , config , ct);

            // 更新运行时内置策略实例
            var builtInInstances = _cachedStrategies ??= _serviceProvider.GetServices<ISeatingStrategy>().ToList();
            var strategy = builtInInstances.FirstOrDefault(s => s.Id == strategyId);
            if (strategy is not null)
            {
                strategy.Priority = config.Priority;
                strategy.IsEnabled = config.IsEnabled;
                ApplyConfiguration(strategy , config.Parameters);
            }
        }

        #region Strategy Helpers

        private static StrategyDisplayInfo BuildDisplayInfo (
            StrategyManifest manifest ,
            string source ,
            Dictionary<string , StrategyConfig> persisted ,
            ISeatingStrategy? runtimeStrategy)
        {
            var info = new StrategyDisplayInfo
            {
                Id = manifest.Id ,
                DisplayName = manifest.DisplayName ,
                Description = manifest.Description ,
                Author = manifest.Author ,
                Category = manifest.Category ,
                Source = source ,
                DefaultPriority = manifest.DefaultPriority ,
                DefaultEnabled = manifest.DefaultEnabled ,
                Priority = manifest.DefaultPriority ,
                IsEnabled = manifest.DefaultEnabled
            };

            // 用持久化的配置覆盖默认值
            if (persisted.TryGetValue(manifest.Id , out var savedConfig))
            {
                info.Priority = savedConfig.Priority;
                info.IsEnabled = savedConfig.IsEnabled;
                info.Parameters = savedConfig.Parameters;
            }
            // 否则从运行时策略实例读取当前参数
            else if (runtimeStrategy is not null)
            {
                info.Parameters = ExtractParameters(runtimeStrategy);
            }

            return info;
        }

        private static Dictionary<string , object?> ExtractParameters (ISeatingStrategy strategy)
        {
            return strategy switch
            {
                FrontRowRotationStrategy fr => new Dictionary<string , object?>
                {
                    ["HistoryWeight"] = fr.Config.HistoryWeight ,
                    ["NeedsFrontRowBonus"] = fr.Config.NeedsFrontRowBonus ,
                    ["FrontRowCount"] = fr.Config.FrontRowCount
                },
                DeskMateStrategy d => new Dictionary<string , object?>
                {
                    ["PreferHorizontal"] = d.Config.PreferHorizontal ,
                    ["AllowVertical"] = d.Config.AllowVertical
                },
                _ => []
            };
        }

        private static void ApplyConfiguration (ISeatingStrategy strategy , Dictionary<string , object?> parameters)
        {
            if (parameters.Count == 0) return;

            switch (strategy)
            {
                case FrontRowRotationStrategy fr:
                    if (parameters.TryGetValue("HistoryWeight" , out var hw) && hw is int hwi)
                        fr.Config.HistoryWeight = hwi;
                    if (parameters.TryGetValue("NeedsFrontRowBonus" , out var nb) && nb is int nbi)
                        fr.Config.NeedsFrontRowBonus = nbi;
                    if (parameters.TryGetValue("FrontRowCount" , out var fc) && fc is int fci)
                        fr.Config.FrontRowCount = fci;
                    break;

                case DeskMateStrategy d:
                    if (parameters.TryGetValue("PreferHorizontal" , out var ph) && ph is bool phb)
                        d.Config.PreferHorizontal = phb;
                    if (parameters.TryGetValue("AllowVertical" , out var av) && av is bool avb)
                        d.Config.AllowVertical = avb;
                    break;
            }
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// 根据 <see cref="SeatingRequest"/> 中的布局类型和参数构建座位列表。
        /// </summary>
        /// <param name="request">座位生成请求。</param>
        /// <returns>生成的座位列表。</returns>
        private List<Seat> BuildSeatsFromRequest (SeatingRequest request)
        {
            ClassroomLayoutDefinition layout = request.LayoutType switch
            {
                LayoutType.Grid => BuildGridLayout(request.LayoutParameters),
                LayoutType.Polar => BuildPolarLayout(request.LayoutParameters),
                LayoutType.Freeform => BuildFreeformLayout(request.LayoutParameters),
                _ => BuildGridLayout(new Dictionary<string , object> { ["Rows"] = 3 , ["Columns"] = 3 })
            };

            ObstacleProcessor.ApplyObstacles(layout);
            return layout.Seats;
        }

        /// <summary>
        /// 从参数字典构建网格布局。
        /// </summary>
        /// <param name="parameters">布局参数字典，支持 "Rows" 和 "Columns" 键。</param>
        /// <returns>网格布局定义。</returns>
        private ClassroomLayoutDefinition BuildGridLayout (Dictionary<string , object> parameters)
        {
            int rows = GetParameter(parameters , "Rows" , 3);
            int columns = GetParameter(parameters , "Columns" , 3);
            var meta = new GridLayoutMetadata
            {
                Rows = rows ,
                Columns = columns ,
                SeatsPerDesk = GetParameter(parameters , "SeatsPerDesk" , 1) ,
                HorizontalSpacing = GetParameter(parameters , "HorizontalSpacing" , 1.0) ,
                VerticalSpacing = GetParameter(parameters , "VerticalSpacing" , 1.0) ,
                IntraDeskSpacing = GetParameter(parameters , "IntraDeskSpacing" , 0.0) ,
                InterDeskSpacing = GetParameter(parameters , "InterDeskSpacing" , 10.0) ,
                OriginX = GetParameter(parameters , "OriginX" , 0.0) ,
                OriginY = GetParameter(parameters , "OriginY" , 0.0) ,
                ColumnRowCounts = GetParameter<List<int>>(parameters , "ColumnRowCounts" , []) ,
                AisleAfterColumns = GetParameter<List<int>>(parameters , "AisleAfterColumns" , []) ,
                AisleAfterRows = GetParameter<List<int>>(parameters , "AisleAfterRows" , []) ,
                AisleWidth = GetParameter(parameters , "AisleWidth" , 0.0) ,
                FrontRowCount = GetParameter(parameters , "FrontRowCount" , 1) ,
                HasPodium = GetParameter(parameters , "HasPodium" , false) ,
                PodiumWidth = GetParameter(parameters , "PodiumWidth" , 0.0) ,
                PodiumHeight = GetParameter(parameters , "PodiumHeight" , 0.0) ,
                EmptyPositions = GetParameter<List<GridPosition>>(parameters , "EmptyPositions" , [])
            };
            return GridLayoutBuilder.BuildGrid(meta);
        }

        /// <summary>
        /// 从参数字典构建极坐标（环形）布局。
        /// </summary>
        /// <param name="parameters">布局参数字典，支持 "RadiusStep"、"Rings" 和 "SeatsPerRing" 键。</param>
        /// <returns>极坐标布局定义。</returns>
        private ClassroomLayoutDefinition BuildPolarLayout (Dictionary<string , object> parameters)
        {
            var meta = new PolarLayoutMetadata
            {
                RadiusStep = GetParameter(parameters , "RadiusStep" , 1.0) ,
                Rings = GetParameter(parameters , "Rings" , 2) ,
                SeatsPerRing = GetParameter(parameters , "SeatsPerRing" , 8) ,
                RingSeatCounts = GetParameter<List<int>>(parameters , "RingSeatCounts" , []) ,
                StartAngleDegrees = GetParameter(parameters , "StartAngleDegrees" , 0.0) ,
                EndAngleDegrees = GetParameter(parameters , "EndAngleDegrees" , 180.0) ,
                EmptyPositions = GetParameter<List<PolarRingAngle>>(parameters , "EmptyPositions" , [])
            };
            return PolarLayoutBuilder.BuildPolar(meta);
        }

        /// <summary>
        /// 从参数字典构建自由形式布局。
        /// </summary>
        /// <param name="parameters">布局参数字典，支持 "Points" 键（坐标点列表）。</param>
        /// <returns>自由形式布局定义。</returns>
        private ClassroomLayoutDefinition BuildFreeformLayout (Dictionary<string , object> parameters)
        {
            var points = new List<(double X , double Y , int? Row , int? Column , int? GroupId)>();
            if (parameters.TryGetValue("Points" , out var rawPoints) && rawPoints is System.Collections.IList list)
            {
                foreach (var item in list)
                {
                    if (item is Dictionary<string , object> dict)
                    {
                        double x = GetParameter(dict , "X" , 0.0);
                        double y = GetParameter(dict , "Y" , 0.0);
                        int? row = dict.ContainsKey("Row") ? GetParameter<int>(dict , "Row" , 0) : null;
                        int? col = dict.ContainsKey("Column") ? GetParameter<int>(dict , "Column" , 0) : null;
                        int? groupId = dict.ContainsKey("GroupId") ? GetParameter<int>(dict , "GroupId" , 0) : null;
                        points.Add((x , y , row , col , groupId));
                    }
                }
            }
            return FreeformLayoutBuilder.BuildFreeform(points);
        }

        /// <summary>
        /// 从字典中安全地获取指定键的值，若不存在或类型转换失败则返回默认值。
        /// </summary>
        /// <typeparam name="T">期望的值类型。</typeparam>
        /// <param name="parameters">参数字典。</param>
        /// <param name="key">键名。</param>
        /// <param name="defaultValue">默认值。</param>
        /// <returns>转换后的值或默认值。</returns>
        private T GetParameter<T> (Dictionary<string , object> parameters , string key , T defaultValue)
        {
            if (parameters.TryGetValue(key , out var value))
            {
                try
                {
                    if (value is T typedValue) return typedValue;
                    return (T)Convert.ChangeType(value , typeof(T));
                }
                catch { return defaultValue; }
            }
            return defaultValue;
        }

        #endregion

        #region Plugin Management

        /// <inheritdoc />
        public async Task<List<PluginDisplayInfo>> GetPluginsAsync (CancellationToken ct = default)
        {
            var loadedPlugins = await _pluginManager.LoadStrategyPluginsAsync(ct);
            var result = new List<PluginDisplayInfo>();
            foreach (var pi in loadedPlugins)
            {
                var iconPath = Path.Combine(pi.PluginPath , "icon.png");
                result.Add(new PluginDisplayInfo
                {
                    Id = pi.Manifest.Id ,
                    Name = pi.Manifest.Name ,
                    Version = pi.Manifest.Version ,
                    Category = pi.Manifest.Category ,
                    LoadKind = GetLoadKind(pi.Manifest) ,
                    IsEnabled = pi.Strategy.IsEnabled ,
                    Description = pi.Manifest.Description ,
                    Author = pi.Manifest.Author ,
                    Priority = pi.Strategy.Priority ,
                    ScriptType = pi.Manifest.ScriptType?.ToLowerInvariant() ,
                    PluginPath = pi.PluginPath ,
                    IconPath = File.Exists(iconPath) ? iconPath : null
                });
            }
            return result;
        }

        /// <inheritdoc />
        public async Task<string> GetPluginScriptAsync (string pluginId , CancellationToken ct = default)
        {
            var manifest = _pluginManager.GetManifest(pluginId)
                ?? throw new InvalidOperationException($"插件 {pluginId} 未加载");
            if (string.IsNullOrEmpty(manifest.ScriptFile))
                throw new InvalidOperationException($"插件 {pluginId} 不是脚本插件");

            var plugin = _pluginManager.GetLoadedPlugin(pluginId)
                ?? throw new InvalidOperationException($"插件 {pluginId} 未找到");
            var scriptPath = Path.Combine(plugin.PluginPath , manifest.ScriptFile);
            return await File.ReadAllTextAsync(scriptPath , ct);
        }

        /// <inheritdoc />
        public async Task SavePluginScriptAsync (string pluginId , string script , CancellationToken ct = default)
        {
            var manifest = _pluginManager.GetManifest(pluginId)
                ?? throw new InvalidOperationException($"插件 {pluginId} 未加载");
            if (string.IsNullOrEmpty(manifest.ScriptFile))
                throw new InvalidOperationException($"插件 {pluginId} 不是脚本插件");

            var plugin = _pluginManager.GetLoadedPlugin(pluginId)
                ?? throw new InvalidOperationException($"插件 {pluginId} 未找到");
            var scriptPath = Path.Combine(plugin.PluginPath , manifest.ScriptFile);
            await File.WriteAllTextAsync(scriptPath , script , ct);
        }

        /// <inheritdoc />
        public async Task<string> GetPluginConfigJsonAsync (string pluginId , CancellationToken ct = default)
        {
            var config = await _pluginConfigService.LoadConfigurationAsync<object>(pluginId , ct);
            return System.Text.Json.JsonSerializer.Serialize(config , new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        /// <inheritdoc />
        public async Task SavePluginConfigJsonAsync (string pluginId , string json , CancellationToken ct = default)
        {
            // 验证 JSON 格式
            var obj = System.Text.Json.JsonSerializer.Deserialize<object>(json)
                ?? throw new ArgumentException("JSON 格式无效");
            await _pluginConfigService.SaveConfigurationAsync(pluginId , obj , ct);
        }

        /// <inheritdoc />
        public async Task SetPluginEnabledAsync (string pluginId , bool enabled , CancellationToken ct = default)
        {
            var manifest = _pluginManager.GetManifest(pluginId)
                ?? throw new InvalidOperationException($"插件 {pluginId} 未加载");

            var plugin = _pluginManager.GetLoadedPlugin(pluginId)
                ?? throw new InvalidOperationException($"插件 {pluginId} 未找到");

            // 更新运行时策略和内存中的清单
            plugin.Strategy.IsEnabled = enabled;
            manifest.Enabled = enabled;

            // 持久化到 manifest 文件
            var manifestPath = Path.Combine(plugin.PluginPath , "plugin.manifest.json");
            var json = System.Text.Json.JsonSerializer.Serialize(manifest , new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(manifestPath , json , ct);
        }

        /// <inheritdoc />
        public Task<PluginManifest?> GetPluginManifestAsync (string pluginId , CancellationToken ct = default)
        {
            var manifest = _pluginManager.GetManifest(pluginId);
            return Task.FromResult(manifest);
        }

        private static string GetLoadKind (PluginManifest manifest)
        {
            if (!string.IsNullOrEmpty(manifest.ScriptFile))
                return manifest.ScriptType?.ToLowerInvariant() switch
                {
                    "lua" => "lua",
                    "csharp" => "csharp",
                    _ => "script"
                };
            if (!string.IsNullOrEmpty(manifest.Assembly))
                return "assembly";
            return "unknown";
        }

        #endregion
    }
}