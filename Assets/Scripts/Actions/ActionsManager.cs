using Core.Command;
using Core.Input;
using Core.Selecting;
using System;
using UnityEngine;

namespace Actions
{
    public class ActionsManager : MonoBehaviour
    {
        private InputService _inputService;
        private CommandService _commandService;
        private SelectingService _selectingService;

        public Action UpdateEvent;

        private DragAction _dragAction;
        private UndoAction _undoAction;
        private RedoAction _redoAction;
        private SelectionDrawAction _selectionDrawAction;
        private SelectAction _selectAction;
        private DeleteAction _deleteAction;

        private void Start()
        {
            _inputService = Core.SystemCore.InputService;
            _commandService = Core.SystemCore.CommandService;
            _selectingService = Core.SystemCore.SelectingService;
            CreateActions();
        }

        private void Update()
        {
            UpdateEvent?.Invoke();
        }

        private void CreateActions()
        {
            _dragAction = new DragAction(_inputService, _commandService, this, _selectingService);
            _undoAction = new UndoAction(_commandService, _inputService);
            _redoAction = new RedoAction(_commandService, _inputService);
            _deleteAction = new DeleteAction(_commandService, _inputService, _selectingService);
            _selectionDrawAction = new SelectionDrawAction(_inputService, this, _selectingService, DrawingSelectionManager.Instance);
            _selectAction = new SelectAction(_inputService, this, _selectingService);
        }
    }
}