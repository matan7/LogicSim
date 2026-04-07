using Core.Command;
using Core.Input;
using UnityEngine.InputSystem;

namespace Actions
{
    public class UndoAction
    {
        private CommandService _commandSerivice;
        private InputService _inputSerivice;
        public UndoAction(CommandService commandService, InputService inputService)
        {
            _commandSerivice = commandService;
            _inputSerivice = inputService;
            _inputSerivice.CurrentInputActions.FindAction("Commands/UndoCommand").performed += OnUndoShortcutPerformed;
        }

        private void OnUndoShortcutPerformed(InputAction.CallbackContext context)
        {
            _commandSerivice.UndoCommand();
        }
    }
}
