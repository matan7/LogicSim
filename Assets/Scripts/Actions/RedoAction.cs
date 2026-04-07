using Core.Command;
using Core.Input;
using UnityEngine.InputSystem;

namespace Actions
{
    public class RedoAction
    {
        private CommandService _commandSerivice;
        private InputService _inputSerivice;
        public RedoAction(CommandService commandService, InputService inputService)
        {
            _commandSerivice = commandService;
            _inputSerivice = inputService;
            _inputSerivice.CurrentInputActions.FindAction("RedoCommand").performed += OnRedoShortcutPerformed;
        }

        private void OnRedoShortcutPerformed(InputAction.CallbackContext context)
        {
            _commandSerivice.RedoCommand();
        }
    }
}
