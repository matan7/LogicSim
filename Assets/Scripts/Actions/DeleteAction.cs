using Core.Input;
using UnityEngine.InputSystem;

namespace Actions
{
    public class DeleteAction
    {
        public DeleteAction(InputService inputService)
        {
            var action = inputService.Find("Commands/DeleteCommand");
            if (action != null)
                action.performed += OnDeletePerformed;
        }

        private void OnDeletePerformed(InputAction.CallbackContext context)
        {
            if (EditCommands.IsTextInputFocused())
                return;
            EditCommands.Delete();
        }
    }
}
