using Core.Command;
using Core.Input;
using Commands;
using UnityEngine.InputSystem;
using Core.Selecting;
using UnityEngine;
using System.Collections.Generic;

namespace Actions
{
    public class DeleteAction
    {
        private CommandService _commandService;
        private InputService _inputService;
        private SelectingService _selectingService;

        public DeleteAction(CommandService commandService, InputService inputService, SelectingService selectingService)
        {
            _commandService = commandService;
            _inputService = inputService;
            _selectingService = selectingService;
            _inputService.CurrentInputActions.FindAction("Commands/DeleteCommand").performed += OnDeletePerformed;
        }

        private void OnDeletePerformed(InputAction.CallbackContext context)
        {
            if (_selectingService.SelectionList.Count > 0) 
            { 
                List<GameObject> objects = new List<GameObject>();
                for (int i = 0; i < _selectingService.SelectionList.Count; i++) 
                    objects.Add(_selectingService.SelectionList[i].Transform.gameObject);
                DeleteCommand command = new DeleteCommand(objects);
                _commandService.ExecuteCommand(command);
            }
        }
    }
}

