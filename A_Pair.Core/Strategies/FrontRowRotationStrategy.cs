using A_Pair.Core.Models;
using A_Pair.Core.Workspace;

namespace A_Pair.Core.Strategies
{
    public class FrontRowRotationStrategy (FrontRowRotationStrategy.FrontRowRotationConfiguration config) : ISeatingStrategy
    {
        private readonly FrontRowRotationConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));

        public FrontRowRotationStrategy () : this(new FrontRowRotationConfiguration()) { }

        public string Id { get; } = "FrontRowRotation";
        public string Name { get; } = "FrontRowRotation";
        public int Priority { get; set; } = 30;
        public bool IsEnabled { get; set; } = true;

        public Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            var emptySeats = workspace.GetEmptySeats().ToList();
            if (emptySeats.Count == 0)
                return Task.FromResult(new StrategyExecutionResult { Success = true });

            // 识别前排座位（对于GridSeat，Row最小的行视为前排）
            var gridSeats = emptySeats.OfType<GridSeat>().ToList();
            if (gridSeats.Count == 0)
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

                int score = (s.NeedsFrontRow ? _config.NeedsFrontRowBonus : 0)
            + s.FrontRowPreferenceScore
            - (frontRowHistoryCount * _config.HistoryWeight);
                return new { Student = s , Score = score };
            }).OrderByDescending(x => x.Score).ToList();

            int assignCount = Math.Min(frontSeats.Count , studentScores.Count);
            for (int i = 0; i < assignCount && !cancellationToken.IsCancellationRequested; i++)
            {
                workspace.TryAssignSeat(frontSeats[i].Id , studentScores[i].Student.Id , out _);
            }

            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        public ValidationResult ValidateConfiguration ()
        {
            if (_config.HistoryWeight < 0)
            {
                return new ValidationResult { IsValid = false , Error = "HistoryWeight must be non-negative." };
            }
            return new ValidationResult { IsValid = true };
        }
        public class FrontRowRotationConfiguration
        {
            /// <summary>
            /// 历史座位权重系数（每次前排扣除分数 = HistoryWeight）
            /// </summary>
            public int HistoryWeight { get; set; } = 10;

            /// <summary>
            /// 特殊需求（如视力需求）的固定加分
            /// </summary>
            public int NeedsFrontRowBonus { get; set; } = 1000;
        }
    }
}