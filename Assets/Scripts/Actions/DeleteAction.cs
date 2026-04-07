using Core.Command;
using Core.Input;
using Commands;
using UnityEngine.InputSystem;

namespace Actions
{
    public class DeleteAction
    {
        private CommandService _commandSerivice;
        private InputService _inputSerivice;
        public DeleteAction(CommandService commandService, InputService inputService)
        {
            _commandSerivice = commandService;
            _inputSerivice = inputService;
            _inputSerivice.CurrentInputActions.FindAction("Commands/DeleteCommand").performed += OnDeletePerformed;
        }

        private void OnDeletePerformed(InputAction.CallbackContext context)
        {

            //DeleteCommand command = new DeleteCommand();
        }
    }
}

