using Core.Settings;
using System;
using System.Collections.Generic;

namespace Core.Command
{
    public class CommandService
    {
        public delegate void HistoryChangedHandler(int undoCount, int redoCount);

        public int UndoCount => _undoList.Count;
        public int RedoCount => _redoList.Count;    

        private LinkedList<ICommand> _undoList = new LinkedList<ICommand>();
        private LinkedList<ICommand> _redoList = new LinkedList<ICommand>();

        /// <summary>
        /// Fired after an undo or redo operation. Parameters are the numbers of remaining undo and redo steps.
        /// </summary>
        public event HistoryChangedHandler HistoryChanged;

        /// <summary>
        /// Execute a command object directly and save to the undo stack.
        /// </summary>
        /// <param name="command"></param>
        public void ExecuteCommand(ICommand command)
        {
            command.Execute();
            _undoList.AddFirst(command);
            if (_undoList.Count > SettingsService.Settings.UndoHistoryMoves)
            {
                _undoList.Last.Value.Dispose();
                _undoList.RemoveLast(); 
            }

            // clear out the redo stack if we make a new move
            foreach (var redoCommand in _redoList)
                redoCommand.Dispose();
            _redoList.Clear();

            HistoryChanged?.Invoke(_undoList.Count, _redoList.Count);
        }

        public void UndoCommand()
        {
            if (_undoList.Count > 0)
            {
                ICommand command = _undoList.First.Value;
                _undoList.RemoveFirst();
                _redoList.AddFirst(command);
                command.Undo();
                HistoryChanged?.Invoke(_undoList.Count, _redoList.Count);
            }
        }

        public void RedoCommand()
        {
            if (_redoList.Count > 0)
            {
                ICommand command = _redoList.First.Value;
                _redoList.RemoveFirst();
                _undoList.AddFirst(command);
                command.Execute();
                HistoryChanged?.Invoke(_undoList.Count, _redoList.Count);    
            }
        }
    }
}
