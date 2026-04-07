using Core.Command;
using Core.Input;
using System;
using UnityEngine;

namespace Actions
{
    public class ActionsManager : MonoBehaviour
    {
        private InputService _inputService;
        private CommandService _commandService;

        public Action UpdateEvent;

        private void Start()
        {
            _inputService = Core.SystemCore.InputService;
            _commandService = Core.SystemCore.CommandService;
            CreateActions();
        }

        private void Update()
        {
            UpdateEvent?.Invoke();
        }

        private void CreateActions()
        {
            DragAction dragAction = new DragAction(_inputService, _commandService, this);
            UndoAction undoAction = new UndoAction(_commandService, _inputService);
            RedoAction redoAction = new RedoAction(_commandService, _inputService);
        }
    }
}