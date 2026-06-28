using SeatFlow.Core.Strategies;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NLua.Exceptions;

namespace SeatFlow.Application.Scripting.Lua
{
    public class LuaScriptStrategy : ISeatingStrategy
    {
        private readonly string _scriptCode;
        private readonly LuaScriptConfiguration _config;
        private readonly ILogger<LuaScriptStrategy> _logger;

        public LuaScriptStrategy (string scriptCode , LuaScriptConfiguration? config = null , ILogger<LuaScriptStrategy>? logger = null)
        {
            _scriptCode = scriptCode ?? throw new ArgumentNullException(nameof(scriptCode));
            _config = config ?? new LuaScriptConfiguration();
            _logger = logger ?? NullLogger<LuaScriptStrategy>.Instance;
            Id = Guid.NewGuid().ToString();
            Name = _config.StrategyName ?? "LuaScript";
            Priority = _config.Priority;
            IsEnabled = _config.Enabled;
        }

        /// <inheritdoc />
        public string Id { get; }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public int Priority { get; set; }

        /// <inheritdoc />
        public bool IsEnabled { get; set; }

        /// <inheritdoc />
        public async Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_config.TimeoutMilliseconds);

            try
            {
                _logger.LogInformation("Lua 脚本策略开始执行：{Name}" , Name);
                using var lua = CreateRestrictedLuaState();

                var api = new LuaWorkspaceAPI(workspace);
                lua["workspace"] = api;
                lua["cancellationToken"] = cancellationToken;

                await Task.Run(() => lua.DoString(_scriptCode) , cts.Token);

                _logger.LogInformation("Lua 脚本策略执行完成：{Name}" , Name);
                return new StrategyExecutionResult { Success = true };
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Lua 脚本执行超时：{Name}（{Timeout}ms）" , Name , _config.TimeoutMilliseconds);
                return new StrategyExecutionResult { Success = false , Message = "脚本执行超时" };
            }
            catch (LuaScriptException ex)
            {
                _logger.LogWarning(ex , "Lua 脚本错误：{Name}" , Name);
                return new StrategyExecutionResult { Success = false , Message = $"Lua 错误: {ex.Message}" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex , "Lua 脚本执行失败：{Name}" , Name);
                return new StrategyExecutionResult { Success = false , Message = $"执行失败: {ex.Message}" };
            }
        }

        /// <inheritdoc />
        public ValidationResult ValidateConfiguration ()
        {
            if (string.IsNullOrWhiteSpace(_scriptCode))
                return new ValidationResult { IsValid = false , Error = "脚本代码不能为空" };
            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// 创建受限的 Lua 状态，移除危险库以防止恶意操作。
        /// </summary>
        /// <remarks>
        /// 当前移除的全局库：<c>io</c>（文件操作）、<c>os</c>（系统操作）、
        /// <c>package</c>（模块加载）、<c>debug</c>（调试功能）。
        /// 内存限制尚未实现（TODO）。
        /// </remarks>
        /// <returns>受限的 Lua 状态实例。</returns>
        private static global::NLua.Lua CreateRestrictedLuaState ()
        {
            var lua = new global::NLua.Lua();
            lua.DoString(@"
        io = nil
        os = nil
        package = nil
        debug = nil
    ");
            //TODO:GC还没有找到好的办法，暂时不限制内存，后续可以考虑通过监控 Lua 内存使用情况来实现
            return lua;
        }
    }

    /// <summary>
    /// 暴露给 Lua 脚本的受限工作区 API，提供学生查询、座位查询和座位分配功能。
    /// </summary>
    /// <remarks>
    /// Lua 脚本通过全局变量 <c>workspace</c> 访问此 API 的方法。
    /// 所有方法均设计为简单类型输入/输出，以兼容 Lua 的类型系统。
    /// </remarks>
    /// <param name="workspace">当前座位工作区。</param>
    public class LuaWorkspaceAPI (SeatingWorkspace workspace)
    {
        private readonly SeatingWorkspace _workspace = workspace;

        /// <summary>
        /// 获取所有未分配的学生 ID 列表。
        /// </summary>
        /// <returns>未分配学生的 ID 数组。</returns>
        public string[] GetUnassignedStudentIds ()
        {
            var assignedIds = _workspace.BuildSeatingPlan().Assignments.Values;
            return [.. _workspace.Students
                .Select(s => s.Id)
                .Where(id => !assignedIds.Contains(id))];
        }

        /// <summary>
        /// 获取所有空座位 ID 列表。
        /// </summary>
        /// <returns>空座位的 ID 数组。</returns>
        public string[] GetEmptySeatIds ()
        {
            return [.. _workspace.GetEmptySeats().Select(s => s.Id)];
        }

        /// <summary>
        /// 将指定学生分配到指定座位。
        /// </summary>
        /// <param name="seatId">座位 ID。</param>
        /// <param name="studentId">学生 ID。</param>
        /// <returns>如果分配成功则返回 true；否则返回 false。</returns>
        public bool AssignSeat (string seatId , string studentId)
        {
            return _workspace.TryAssignSeat(seatId , studentId , out _);
        }

        /// <summary>
        /// 获取指定学生的基本信息。
        /// </summary>
        /// <param name="studentId">学生 ID。</param>
        /// <returns>学生信息对象；如果未找到则返回 <c>null</c>。</returns>
        public StudentInfo? GetStudent (string studentId)
        {
            var student = _workspace.Students.FirstOrDefault(s => s.Id == studentId);
            if (student == null) return null;
            return new StudentInfo
            {
                Id = student.Id ,
                Name = student.Name ,
                Height = student.Height ,
                NeedsFrontRow = student.NeedsFrontRow ,
                FrontRowPreferenceScore = student.FrontRowPreferenceScore
            };
        }

        /// <summary>
        /// 获取指定座位的基本信息。
        /// </summary>
        /// <param name="seatId">座位 ID。</param>
        /// <returns>座位信息对象；如果未找到则返回 <c>null</c>。</returns>
        public SeatInfo? GetSeat (string seatId)
        {
            var seat = _workspace.FindSeats(s => s.Id == seatId).FirstOrDefault();
            if (seat == null) return null;
            return new SeatInfo
            {
                Id = seat.Id ,
                IsAvailable = seat.IsAvailable ,
                IsFixed = seat.IsFixed ,
                OccupantId = seat.OccupantId
            };
        }
    }

    /// <summary>
    /// 用于 Lua 交互的学生数据传输对象。
    /// </summary>
    public class StudentInfo
    {
        /// <summary>学生 ID。</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>学生姓名。</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>学生身高（可选）。</summary>
        public float? Height { get; set; }
        /// <summary>是否需要前排座位。</summary>
        public bool NeedsFrontRow { get; set; }
        /// <summary>前排偏好分数。</summary>
        public int FrontRowPreferenceScore { get; set; }
    }

    /// <summary>
    /// 用于 Lua 交互的座位数据传输对象。
    /// </summary>
    public class SeatInfo
    {
        /// <summary>座位 ID。</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>座位是否可用。</summary>
        public bool IsAvailable { get; set; }
        /// <summary>座位是否固定。</summary>
        public bool IsFixed { get; set; }
        /// <summary>当前占用学生的 ID（可选）。</summary>
        public string? OccupantId { get; set; }
    }

    /// <summary>
    /// Lua 脚本策略的配置选项。
    /// </summary>
    public class LuaScriptConfiguration
    {
        /// <summary>策略显示名称。</summary>
        public string? StrategyName { get; set; }
        /// <summary>策略在管道中的执行优先级。</summary>
        public int Priority { get; set; } = 50;
        /// <summary>是否启用策略。</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>脚本执行超时时间（毫秒），默认 5000。</summary>
        public int TimeoutMilliseconds { get; set; } = 5000;
        /// <summary>内存限制（字节），默认 10 MB。</summary>
        public int MemoryLimitBytes { get; set; } = 10 * 1024 * 1024;
    }
}