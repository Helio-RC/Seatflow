using A_Pair.Application.Commands;
using A_Pair.Core.Workspace;

namespace A_Pair.Application.Services
{
    /// <summary>
    /// 命令历史记录管理器，支持撤销（Undo）和重做（Redo）操作。
    /// </summary>
    /// <remarks>
    /// 内部维护两个栈：<see cref="_undo"/> 存储已执行的命令，<see cref="_redo"/> 存储已撤销的命令。
    /// 执行新命令时会清空 <see cref="_redo"/> 栈，确保撤销/重做历史的一致性。
    /// </remarks>
    public class CommandHistory
    {
        private readonly Stack<IUndoableCommand> _undo = new();
        private readonly Stack<IUndoableCommand> _redo = new();

        /// <summary>
        /// 获取一个值，指示当前是否有可撤销的命令。
        /// </summary>
        public bool CanUndo => _undo.Count > 0;

        /// <summary>
        /// 获取一个值，指示当前是否有可重做的命令。
        /// </summary>
        public bool CanRedo => _redo.Count > 0;

        /// <summary>
        /// 执行指定的命令，若成功则将其压入撤销栈并清空重做栈。
        /// </summary>
        /// <param name="command">要执行的命令。</param>
        /// <param name="workspace">当前座位工作区。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>如果命令执行成功则返回 true；否则返回 false。</returns>
        public async Task<bool> ExecuteAsync (IUndoableCommand command , SeatingWorkspace workspace , CancellationToken cancellationToken = default)
        {
            var ok = await command.ExecuteAsync(workspace , cancellationToken);
            if (ok)
            {
                _undo.Push(command);
                _redo.Clear();
            }
            return ok;
        }

        /// <summary>
        /// 撤销最近一次执行的命令。仅操作成功后才会从栈中移除命令。
        /// </summary>
        /// <param name="workspace">当前座位工作区。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>如果撤销成功则返回 true；否则返回 false。</returns>
        public async Task<bool> UndoAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default)
        {
            if (!CanUndo) return false;
            var cmd = _undo.Peek();
            var ok = await cmd.UndoAsync(workspace , cancellationToken);
            if (ok)
            {
                _undo.Pop();
                _redo.Push(cmd);
            }
            return ok;
        }

        /// <summary>
        /// 重做最近一次撤销的命令。仅操作成功后才会从栈中移除命令。
        /// </summary>
        /// <param name="workspace">当前座位工作区。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>如果重做成功则返回 true；否则返回 false。</returns>
        public async Task<bool> RedoAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default)
        {
            if (!CanRedo) return false;
            var cmd = _redo.Peek();
            var ok = await cmd.ExecuteAsync(workspace , cancellationToken);
            if (ok)
            {
                _redo.Pop();
                _undo.Push(cmd);
            }
            return ok;
        }
    }
}
