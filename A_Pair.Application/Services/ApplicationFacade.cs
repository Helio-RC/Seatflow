using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using A_Pair.Core.Workspace;
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

        public async Task<List<Student>> LoadStudentsAsync(string source, CancellationToken cancellationToken = default)
        {
            // Resolve IStudentProvider from DI
            var provider = _serviceProvider.GetService<IStudentProvider>();
            if (provider == null)
            {
                return new List<Student>();
            }

            return await provider.LoadAsync(source, cancellationToken);
        }

        public async Task<SeatingWorkspace> GenerateSeatingAsync(SeatingRequest request, IProgress<SeatingProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            // For now create a workspace from provider -> layout (simple)
            var studentProvider = _serviceProvider.GetService<IStudentProvider>();
            var students = studentProvider == null ? new List<Student>() : await studentProvider.LoadAsync(string.Empty, cancellationToken);

            // Build simple grid seats for demonstration
            var seats = new List<Seat>();
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    seats.Add(new GridSeat { Row = r + 1, Column = c + 1 });
                }
            }

            var workspace = new SeatingWorkspace(students, seats);

            // persist as the current workspace for subsequent commands/undo-redo
            _currentWorkspace = workspace;

            // resolve strategies
            var strategies = _serviceProvider.GetService<IEnumerable<Core.Strategies.ISeatingStrategy>>() ?? Enumerable.Empty<Core.Strategies.ISeatingStrategy>();
            var pipeline = new StrategyExecutionPipeline(strategies);
            var plan = await pipeline.ExecuteAsync(workspace, cancellationToken);

            // Save snapshot
            var snapshot = new SeatingSnapshot { SeatAssignments = plan.Assignments };
            await _snapshotRepository.SaveAsync(snapshot);

            return workspace;
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

        public async Task ExportSeatingPlanAsync(SeatingWorkspace plan, string path, CancellationToken cancellationToken = default)
        {
            // Simple export to json
            var repo = new SeatingSnapshotRepository(Path.GetDirectoryName(path) ?? "Assignments");
            var snapshot = new SeatingSnapshot { SeatAssignments = plan.BuildSeatingPlan().Assignments };
            await repo.SaveAsync(snapshot);
        }
    }
}
