using A_Pair.Application.Commands;
using A_Pair.Core.Workspace;

namespace A_Pair.Application.Services
{
    public class CommandHistory
    {
        private readonly Stack<IUndoableCommand> _undo = new();
        private readonly Stack<IUndoableCommand> _redo = new();

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

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

        public async Task<bool> UndoAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default)
        {
            if (!CanUndo) return false;
            var cmd = _undo.Pop();
            var ok = await cmd.UndoAsync(workspace , cancellationToken);
            if (ok) _redo.Push(cmd);
            return ok;
        }

        public async Task<bool> RedoAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default)
        {
            if (!CanRedo) return false;
            var cmd = _redo.Pop();
            var ok = await cmd.ExecuteAsync(workspace , cancellationToken);
            if (ok) _undo.Push(cmd);
            return ok;
        }
    }
}
