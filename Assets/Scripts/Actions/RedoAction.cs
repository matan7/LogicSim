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
            var action = _inputService.Find("Commands/RedoCommand");
            if (action != null)
                action.performed += OnRedoShortcutPerformed;
        }

        private void OnRedoShortcutPerformed(InputAction.CallbackContext context)
        {
            if (!Core.SystemCore.AppStateService.IsEditing || EditCommands.IsTextInputFocused())
                return;
            _commandService.RedoCommand();
        }
    }
}
