using Core.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Command
{
    /// <summary>
    /// Undo/redo history. Scopes nest: an IC opened for editing gets its own history, and closing it hands the
    /// applied commands back to the caller (see <see cref="PopScope"/>).
    /// </summary>
    public class CommandService
    {
        public delegate void HistoryChangedHandler(int undoCount, int redoCount);

        private sealed class Scope
        {
            public readonly LinkedList<ICommand> Undo = new LinkedList<ICommand>();
            public readonly LinkedList<ICommand> Redo = new LinkedList<ICommand>();
        }

        private readonly List<Scope> _scopes = new List<Scope> { new Scope() };

        private Scope Top => _scopes[_scopes.Count - 1];

        public int UndoCount => Top.Undo.Count;
        public int RedoCount => Top.Redo.Count;

        /// <summary>0 for the document; one more for every opened IC.</summary>
        public int ScopeDepth => _scopes.Count - 1;

        /// <summary>
        /// Fired after the history changed. Parameters are the numbers of remaining undo and redo steps.
        /// </summary>
        public event HistoryChangedHandler HistoryChanged;

        /// <summary>Executes a command and adds it to the undo stack.</summary>
        public void ExecuteCommand(ICommand command)
        {
            command.Execute();
            PushExecuted(command);
        }

        /// <summary>Adds a command that has already been executed.</summary>
        public void PushExecuted(ICommand command)
        {
            var scope = Top;
            scope.Undo.AddFirst(command);
            // Nested scopes are handed back as a whole when they close, so only the document history is trimmed.
            if (_scopes.Count == 1 && scope.Undo.Count > SettingsService.Settings.UndoHistoryMoves)
            {
                scope.Undo.Last.Value.Dispose();
                scope.Undo.RemoveLast();
            }

            // A new command invalidates the redo stack.
            foreach (var redoCommand in scope.Redo)
                redoCommand.Dispose();
            scope.Redo.Clear();

            NotifyChanged();
        }

        public void UndoCommand()
        {
            var scope = Top;
            if (scope.Undo.Count > 0)
            {
                ICommand command = scope.Undo.First.Value;
                scope.Undo.RemoveFirst();
                scope.Redo.AddFirst(command);
                command.Undo();
                NotifyChanged();
            }
        }

        public void RedoCommand()
        {
            var scope = Top;
            if (scope.Redo.Count > 0)
            {
                ICommand command = scope.Redo.First.Value;
                scope.Redo.RemoveFirst();
                scope.Undo.AddFirst(command);
                command.Execute();
                NotifyChanged();
            }
        }

        public void PushScope()
        {
            _scopes.Add(new Scope());
            NotifyChanged();
        }

        /// <summary>Removes the innermost scope. Returns its executed commands, oldest first; redo entries are disposed.</summary>
        public IReadOnlyList<ICommand> PopScope()
        {
            if (_scopes.Count == 1)
                throw new InvalidOperationException("The document history cannot be removed");
            var scope = Top;
            _scopes.RemoveAt(_scopes.Count - 1);
            foreach (var command in scope.Redo)
                command.Dispose();
            var executed = scope.Undo.Reverse().ToList();
            NotifyChanged();
            return executed;
        }

        private void NotifyChanged() => HistoryChanged?.Invoke(UndoCount, RedoCount);
    }
}
