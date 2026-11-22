using Core;
using UnityEngine;
using UnityEngine.UI;

namespace LogicSim.UI
{
    /// <summary>App commands a button in the scene can run. The values are serialized: append, never renumber.</summary>
    public enum UiCommand
    {
        None = 0,
        New = 1,
        Open = 2,
        Save = 3,
        SaveAs = 4,
        TruthTable = 5,
        Help = 6,
        Undo = 7,
        Redo = 8,
        Cut = 9,
        Copy = 10,
        Paste = 11,
        Delete = 12,
        Rotate = 13,
        SelectAll = 14,
        CreateIc = 15,
        CollapseIc = 16,
        OpenIc = 17,
        ExplodeIc = 18,
        MakeUnique = 19,
        CloseIcApply = 20,
        CloseIcDiscard = 21,
    }

    /// <summary>
    /// Runs an app command when the button on this object or below it is clicked, so that the scene's buttons need no
    /// code that creates or wires them. Undo and Redo are enabled only while there is something to undo or redo.
    /// </summary>
    public sealed class CommandButton : MonoBehaviour
    {
        [SerializeField] private UiCommand _command;

        private Button _button;

        public UiCommand Command => _command;

        private void Start()
        {
            _button = GetComponentInChildren<Button>(true);
            if (_button == null)
            {
                Debug.LogWarning($"[LogicSim] Command button '{name}' has no Button.", this);
                return;
            }
            _button.onClick.AddListener(() => Run(_command));
            if (_command == UiCommand.Undo || _command == UiCommand.Redo)
            {
                SystemCore.CommandService.HistoryChanged += OnHistoryChanged;
                _button.interactable = false;
            }
        }

        private void OnDestroy()
        {
            if (SystemCore.CommandService != null)
                SystemCore.CommandService.HistoryChanged -= OnHistoryChanged;
        }

        private void OnHistoryChanged(int undoCount, int redoCount)
        {
            if (_button != null)
                _button.interactable = _command == UiCommand.Undo ? undoCount > 0 : redoCount > 0;
        }

        public static void Run(UiCommand command)
        {
            switch (command)
            {
                case UiCommand.New: AppCommands.New(); break;
                case UiCommand.Open: AppCommands.ShowOpen(); break;
                case UiCommand.Save: AppCommands.Save(saveAs: false); break;
                case UiCommand.SaveAs: AppCommands.Save(saveAs: true); break;
                case UiCommand.TruthTable: AppCommands.ShowTruthTable(); break;
                case UiCommand.Help: AppCommands.ShowHelp(); break;
                case UiCommand.Undo: SystemCore.CommandService.UndoCommand(); break;
                case UiCommand.Redo: SystemCore.CommandService.RedoCommand(); break;
                case UiCommand.Cut: Actions.EditCommands.Cut(); break;
                case UiCommand.Copy: Actions.EditCommands.Copy(); break;
                case UiCommand.Paste: Actions.EditCommands.Paste(); break;
                case UiCommand.Delete: Actions.EditCommands.Delete(); break;
                case UiCommand.Rotate: Actions.EditCommands.Rotate(); break;
                case UiCommand.SelectAll: Actions.EditCommands.SelectAll(); break;
                case UiCommand.CreateIc: AppCommands.CreateSubcircuit(); break;
                case UiCommand.CollapseIc: AppCommands.CollapseSelection(); break;
                case UiCommand.OpenIc: AppCommands.OpenSelectedSubcircuit(); break;
                case UiCommand.ExplodeIc: AppCommands.ExplodeSelection(); break;
                case UiCommand.MakeUnique: AppCommands.MakeSelectedUnique(); break;
                case UiCommand.CloseIcApply: AppCommands.CloseSubcircuit(apply: true); break;
                case UiCommand.CloseIcDiscard: AppCommands.CloseSubcircuit(apply: false); break;
            }
        }
    }
}
