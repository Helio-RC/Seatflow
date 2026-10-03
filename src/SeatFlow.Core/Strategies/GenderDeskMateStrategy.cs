using SeatFlow.Core.Enums;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Core.Strategies
{
    /// <summary>
    /// 同桌性别搭配策略（依赖策略，Priority=44，在 RandomFill 上下文中执行）。
    /// 当 RandomFill 提议 (student, seat) 时，检查目标座位的同桌（同行+邻列+同 SeatsPerDesk）
    /// 已就座者的性别：默认要求男女搭配，可切换为同性同桌。
    /// 不符合则请求重掷；重掷次数耗尽后强制分配并记录警告。
    /// 性别未知（未填写）的学生不参与判定。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="GenderRestrictedSeatStrategy"/>（座位级性别限制）不同，
    /// 本策略约束的是同桌两人的组合关系，而非某个座位只允许某个性别。
    /// </remarks>
    public class GenderDeskMateStrategy(
        GenderDeskMateStrategy.GenderDeskMateConfiguration config,
        ILogger<GenderDeskMateStrategy>? logger = null) : IDependentSeatingStrategy
    {
        private readonly GenderDeskMateConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
        private readonly ILogger<GenderDeskMateStrategy> _logger = logger ?? NullLogger<GenderDeskMateStrategy>.Instance;

        /// <summary>每桌座位数（来自会场 GridLayoutMetadata.SeatsPerDesk），用于桌边界判定。</summary>
        private int _seatsPerDesk = 2;

        /// <summary>策略展示名称（与 manifest displayName 一致）。</summary>
        public const string DisplayNameConst = "同桌性别搭配";

        /// <inheritdoc />
        public string Id { get; } = "GenderDeskMate";

        /// <inheritdoc />
        public string Name { get; } = "GenderDeskMate";

        /// <inheritdoc />
        public string DisplayName => DisplayNameConst;

        /// <inheritdoc />
        public int Priority { get; set; } = 44;

        /// <inheritdoc />
        public bool IsEnabled { get; set; } = false;

        /// <summary>获取策略配置对象，供 Application 层读取和修改配置参数。</summary>
        public GenderDeskMateConfiguration Config => _config;

        /// <summary>使用默认配置创建实例。</summary>
        public GenderDeskMateStrategy() : this(new GenderDeskMateConfiguration()) { }

        /// <summary>同步会场每桌座位数，用于桌边界检查。</summary>
        public void SetSeatsPerDesk(int count) => _seatsPerDesk = Math.Max(1, count);

        /// <inheritdoc />
        public Task<DependentEvaluationResult> EvaluateAsync(
            SeatingWorkspace workspace,
            Student student,
            Seat targetSeat,
            IRandomFillContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            ArgumentNullException.ThrowIfNull(student);
            ArgumentNullException.ThrowIfNull(targetSeat);
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            // 固定座位由 FixedSeatStrategy 全权负责，不干涉
            if (targetSeat.IsFixed)
                return Task.FromResult(DependentResult.Approve());

            // 性别未知或"其他"时不参与搭配判定
            if (student.Gender is not { } studentGender
                || studentGender is Gender.Unknown or Gender.Other)
            {
                return Task.FromResult(DependentResult.Approve());
            }

            var deskMateSeats = workspace.FindSeats(s =>
                s.Id != targetSeat.Id
                && !s.IsAvailable
                && s.OccupantId is not null
                && !s.IsFixed
                && SeatAdjacencyHelper.AreDeskMates(s, targetSeat, _seatsPerDesk));

            foreach (var mateSeat in deskMateSeats)
            {
                var mate = workspace.Students.FirstOrDefault(s => s.Id == mateSeat.OccupantId);
                if (mate?.Gender is not { } mateGender
                    || mateGender is Gender.Unknown or Gender.Other)
                {
                    continue;
                }

                // 男女搭配模式：同性冲突；同性同桌模式：异性冲突
                bool conflict = _config.PreferMixed
                    ? mateGender == studentGender
                    : mateGender != studentGender;

                if (!conflict)
                    continue;

                if (context.RerollCount < context.MaxRerolls - 1)
                {
                    _logger.LogDebug(
                        "GenderDeskMate：学生 {Student} 的目标座位 {Seat} 同桌性别不匹配（{MateGender}），请求重掷",
                        student.Name, targetSeat.Id, mateGender);
                    return Task.FromResult(DependentResult.Reject());
                }

                context.LogWarning(Id, DisplayNameConst, "GenderDeskMate_Forced",
                    student.Id, mateSeat.OccupantId!);
                _logger.LogInformation(
                    "GenderDeskMate：重掷耗尽，学生 {Student} 强制分配到 {Seat}，未满足同桌性别搭配",
                    student.Name, targetSeat.Id);
                return Task.FromResult(DependentResult.Approve());
            }

            return Task.FromResult(DependentResult.Approve());
        }

        /// <inheritdoc />
        public ValidationResult ValidateConfiguration() => new() { IsValid = true };

        /// <summary>
        /// 同桌性别搭配策略的配置参数。
        /// </summary>
        public class GenderDeskMateConfiguration
        {
            /// <summary>是否男女搭配（true=男女搭配，false=同性同桌）。</summary>
            public bool PreferMixed { get; set; } = true;
        }
    }
}
