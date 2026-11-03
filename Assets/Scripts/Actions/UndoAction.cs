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
            var action = _inputService.Find("Commands/UndoCommand");
            if (action != null)
                action.performed += OnUndoShortcutPerformed;
        }

        private void OnUndoShortcutPerformed(InputAction.CallbackContext context)
        {
            if (!Core.SystemCore.AppStateService.IsEditing || EditCommands.IsTextInputFocused())
                return;
            _commandService.UndoCommand();
        }
    }
}
