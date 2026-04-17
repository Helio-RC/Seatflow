using System.Collections.Generic;
using A_Pair.Core.Workspace;

namespace A_Pair.Application.Interfaces
{
    /// <summary>
    /// 座位冲突解决器，处理固定座位、重复分配等冲突
    /// </summary>
    public interface IConflictResolver
    {
        /// <summary>
        /// 检测并解决冲突
        /// </summary>
        /// <param name="workspace">当前工作区</param>
        /// <returns>解决结果及冲突详情</returns>
        ConflictResolutionResult Resolve (SeatingWorkspace workspace);
    }

    public class ConflictResolutionResult
    {
        public bool Success { get; set; }
        public List<ConflictInfo> Conflicts { get; set; } = new();
        public List<string> ActionsTaken { get; set; } = new();
    }

    public class ConflictInfo
    {
        public ConflictType Type { get; set; }
        public string SeatId { get; set; } = string.Empty;
        public string StudentId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public enum ConflictType
    {
        FixedSeatMismatch,      // 固定座位学生不匹配
        DuplicateAssignment,    // 学生被重复分配
        SeatOverCapacity,       // 座位超容
        InvalidSeat             // 座位无效
    }
}