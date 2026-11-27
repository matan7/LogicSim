using Core.Input;
using UnityEngine.InputSystem;

namespace Actions
{
    internal class PasteAction
    {
        public PasteAction(InputService inputService)
        {
            var action = inputService.Actions.Commands.PasteCommand;
            if (action != null)
                action.performed += Paste;
        }

        private void Paste(InputAction.CallbackContext context)
        {
            if (EditCommands.KeyboardIsTaken())
                return;
            EditCommands.Paste();
        }
    }
}
