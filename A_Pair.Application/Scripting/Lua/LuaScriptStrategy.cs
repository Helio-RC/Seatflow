using System;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Strategies;
using A_Pair.Core.Workspace;
using NLua;
using NLua.Exceptions;

namespace A_Pair.Application.Scripting.Lua
{
    public class LuaScriptStrategy : ISeatingStrategy
    {
        private readonly string _scriptCode;
        private readonly LuaScriptConfiguration _config;

        public LuaScriptStrategy (string scriptCode , LuaScriptConfiguration? config = null)
        {
            _scriptCode = scriptCode ?? throw new ArgumentNullException(nameof(scriptCode));
            _config = config ?? new LuaScriptConfiguration();
            Id = Guid.NewGuid().ToString();
            Name = _config.StrategyName ?? "LuaScript";
            Priority = _config.Priority;
            IsEnabled = _config.Enabled;
        }

        public string Id { get; }
        public string Name { get; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }

        public Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_config.TimeoutMilliseconds);

            try
            {
                using var lua = CreateRestrictedLuaState();

                // 注入工作区 API
                var api = new LuaWorkspaceAPI(workspace);
                lua["workspace"] = api;
                lua["cancellationToken"] = cancellationToken;

                // 执行脚本（在后台线程中，支持超时）
                var task = Task.Run(() => lua.DoString(_scriptCode) , cts.Token);
                task.Wait(cts.Token);

                return Task.FromResult(new StrategyExecutionResult { Success = true });
            }
            catch (OperationCanceledException)
            {
                return Task.FromResult(new StrategyExecutionResult { Success = false , Message = "脚本执行超时" });
            }
            catch (LuaScriptException ex)
            {
                return Task.FromResult(new StrategyExecutionResult { Success = false , Message = $"Lua 错误: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new StrategyExecutionResult { Success = false , Message = $"执行失败: {ex.Message}" });
            }
        }

        public ValidationResult ValidateConfiguration ()
        {
            if (string.IsNullOrWhiteSpace(_scriptCode))
                return new ValidationResult { IsValid = false , Error = "脚本代码不能为空" };
            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// 创建受限的 Lua 状态，移除危险库
        /// </summary>
        private global::NLua.Lua CreateRestrictedLuaState ()
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
    /// 暴露给 Lua 的工作区 API
    /// </summary>
    public class LuaWorkspaceAPI
    {
        private readonly SeatingWorkspace _workspace;

        public LuaWorkspaceAPI (SeatingWorkspace workspace)
        {
            _workspace = workspace;
        }

        /// <summary>
        /// 获取所有未分配的学生 ID 列表
        /// </summary>
        public string[] GetUnassignedStudentIds ()
        {
            var assignedIds = _workspace.BuildSeatingPlan().Assignments.Values;
            return _workspace.Students
                .Select(s => s.Id)
                .Where(id => !assignedIds.Contains(id))
                .ToArray();
        }

        /// <summary>
        /// 获取所有空座位 ID 列表
        /// </summary>
        public string[] GetEmptySeatIds ()
        {
            return _workspace.GetEmptySeats().Select(s => s.Id).ToArray();
        }

        /// <summary>
        /// 分配座位
        /// </summary>
        public bool AssignSeat (string seatId , string studentId)
        {
            return _workspace.TryAssignSeat(seatId , studentId , out _);
        }

        /// <summary>
        /// 获取学生信息
        /// </summary>
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
        /// 获取座位信息
        /// </summary>
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

    // 用于 Lua 交互的数据传输对象
    public class StudentInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public float? Height { get; set; }
        public bool NeedsFrontRow { get; set; }
        public int FrontRowPreferenceScore { get; set; }
    }

    public class SeatInfo
    {
        public string Id { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
        public bool IsFixed { get; set; }
        public string? OccupantId { get; set; }
    }

    public class LuaScriptConfiguration
    {
        public string? StrategyName { get; set; }
        public int Priority { get; set; } = 50;
        public bool Enabled { get; set; } = true;
        public int TimeoutMilliseconds { get; set; } = 5000;
        public int MemoryLimitBytes { get; set; } = 10 * 1024 * 1024; // 10 MB
    }
}