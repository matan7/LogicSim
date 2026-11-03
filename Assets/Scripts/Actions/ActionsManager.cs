using Core;
using System;
using UnityEngine;

namespace Actions
{
    /// <summary>Creates the input-driven actions and gives them a per-frame update.</summary>
    public class ActionsManager : MonoBehaviour
    {
        public Action UpdateEvent;

        private void Start()
        {
            var input = SystemCore.InputService;
            var selecting = SystemCore.SelectingService;

            // Order matters: the wire tool claims presses on pins before dragging and selection look at them.
            var wireAction = new WireAction(input, this);
            new DragAction(input, this, selecting);
            new SimInteractionAction(input, this);
            new UndoAction(SystemCore.CommandService, input);
            new RedoAction(SystemCore.CommandService, input);
            new DeleteAction(input);
            new SelectionDrawAction(input, this, selecting, DrawingSelectionManager.Instance);
            new SelectAction(input, this, selecting);
            new CopyAction(input);
            new PasteAction(input);
            new ShortcutActions(input, wireAction);
        }

        private void Update()
        {
            UpdateEvent?.Invoke();
        }
    }
}
