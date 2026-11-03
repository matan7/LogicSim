using Core.Input;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Actions
{
    internal class CopyAction
    {
        public CopyAction(InputService inputService)
        {
            var action = inputService.Find("Commands/CopyCommand");
            if (action != null)
                action.performed += Copy;
        }

        private void Copy(InputAction.CallbackContext context)
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && selected.TryGetComponent(out TMP_InputField input))
            {
                // Text fields copy their own selection.
                int start = input.selectionStringAnchorPosition;
                int end = input.selectionStringFocusPosition;
                if (start > end)
                    (start, end) = (end, start);
                if (end > start)
                    Core.SystemCore.ClipboardService.SetStringClipboard(input.text.Substring(start, end - start));
                return;
            }
            EditCommands.Copy();
        }
    }
}
