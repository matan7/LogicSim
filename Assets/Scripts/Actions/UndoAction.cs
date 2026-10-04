using Core.Command;
using Core.Input;
using UnityEngine.InputSystem;

namespace Actions
{
    public class UndoAction
    {
        private CommandService _commandService;
        private InputService _inputService;
        public UndoAction(CommandService commandService, InputService inputService)
        {
            _commandService = commandService;
            _inputService = inputService;
            _inputService.CurrentInputActions.FindAction("Commands/UndoCommand").performed += OnUndoShortcutPerformed;
        }

        private void OnUndoShortcutPerformed(InputAction.CallbackContext context)
        {
            _commandService.UndoCommand();
        }
    }
}
