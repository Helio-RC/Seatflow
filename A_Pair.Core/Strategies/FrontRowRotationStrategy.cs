using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Models;
using A_Pair.Core.Workspace;

namespace A_Pair.Core.Strategies
{
    public class FrontRowRotationStrategy : ISeatingStrategy
    {
        public FrontRowRotationStrategy()
        {
            Id = "FrontRowRotation";
            Name = "FrontRowRotation";
            Priority = 30;
            IsEnabled = true;
        }

        public string Id { get; }
        public string Name { get; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }

        public Task<StrategyExecutionResult> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken)
        {
            if (workspace is null) throw new ArgumentNullException(nameof(workspace));

            var emptySeats = workspace.GetEmptySeats().ToList();
            if (!emptySeats.Any())
                return Task.FromResult(new StrategyExecutionResult { Success = true });

            // 识别前排座位（对于GridSeat，Row最小的行视为前排）
            var gridSeats = emptySeats.OfType<GridSeat>().ToList();
            if (!gridSeats.Any())
                return Task.FromResult(new StrategyExecutionResult { Success = true });

            int frontRow = gridSeats.Min(s => s.Row);
            var frontSeats = gridSeats.Where(s => s.Row == frontRow).ToList();

            // 获取尚未分配的学生
            var assignedStudentIds = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();
            var availableStudents = workspace.Students.Where(s => !assignedStudentIds.Contains(s.Id)).ToList();

            // 计算每个学生对前排的“需求度分数”
            var studentScores = availableStudents.Select(s =>
            {
                int frontRowHistoryCount = s.RecentSeatHistory.GetAll()
                    .Count(seatId =>
                    {
                        var histSeat = workspace.FindSeats(seat => seat.Id == seatId).FirstOrDefault();
                        return histSeat is GridSeat gs && gs.Row == frontRow;
                    });

                int score = (s.NeedsFrontRow ? 1000 : 0)
                            + s.FrontRowPreferenceScore
                            - (frontRowHistoryCount * 10);
                return new { Student = s, Score = score };
            }).OrderByDescending(x => x.Score).ToList();

            int assignCount = Math.Min(frontSeats.Count, studentScores.Count);
            for (int i = 0; i < assignCount && !cancellationToken.IsCancellationRequested; i++)
            {
                workspace.TryAssignSeat(frontSeats[i].Id, studentScores[i].Student.Id, out _);
            }

            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        public ValidationResult ValidateConfiguration()
        {
            // 该策略无特殊配置，默认有效
            return new ValidationResult { IsValid = true };
        }
    }
}