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
        private CommandService _commandSerivice;
        private InputService _inputSerivice;
        private SelectingService _selectingService;

        public DeleteAction(CommandService commandService, InputService inputService, SelectingService selectingService)
        {
            _commandSerivice = commandService;
            _inputSerivice = inputService;
            _selectingService = selectingService;
            _inputSerivice.CurrentInputActions.FindAction("Commands/DeleteCommand").performed += OnDeletePerformed;
        }

        private void OnDeletePerformed(InputAction.CallbackContext context)
        {
            if (_selectingService.SelectionList.Count > 0) 
            { 
                List<GameObject> objects = new List<GameObject>();
                for (int i = 0; i < _selectingService.SelectionList.Count; i++) 
                    objects.Add(_selectingService.SelectionList[i].Transform.gameObject);
                DeleteCommand command = new DeleteCommand(objects);
                _commandSerivice.ExecuteCommand(command);
            }
        }
    }
}

