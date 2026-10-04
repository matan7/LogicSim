using Core.Command;
using Core.Input;
using UnityEngine.InputSystem;

namespace Actions
{
    public class RedoAction
    {
        private CommandService _commandService;
        private InputService _inputService;
        public RedoAction(CommandService commandService, InputService inputService)
        {
            _commandService = commandService;
            _inputService = inputService;
            _inputService.CurrentInputActions.FindAction("RedoCommand").performed += OnRedoShortcutPerformed;
        }

        private void OnRedoShortcutPerformed(InputAction.CallbackContext context)
        {
            _commandService.RedoCommand();
        }
    }
}
