using System;
using Core;
using Core.Input;
using LogicSim.UI;
using UnityEngine.InputSystem;

namespace Actions
{
    /// <summary>Keyboard shortcuts that map to app commands.</summary>
    public class ShortcutActions
    {
        private readonly WireAction _wireAction;

        public ShortcutActions(InputService inputService, WireAction wireAction)
        {
            _wireAction = wireAction;
            Bind(inputService, "Commands/CutCommand", () => EditCommands.Cut());
            Bind(inputService, "Commands/SelectAllCommand", EditCommands.SelectAll);
            Bind(inputService, "Commands/RotateCommand", () =>
            {
                // R is also part of Ctrl+R (redo).
                if (Keyboard.current != null && Keyboard.current.ctrlKey.isPressed)
                    return;
                EditCommands.Rotate();
            });
            Bind(inputService, "Commands/SaveCommand", () => AppCommands.Save(saveAs: false));
            Bind(inputService, "Commands/OpenCommand", AppCommands.ShowOpen);
            Bind(inputService, "Commands/NewCommand", AppCommands.New);
            Bind(inputService, "Commands/ToggleSimulation", AppCommands.ToggleSimulation);
            Bind(inputService, "Commands/PauseSimulation", AppCommands.TogglePause);
            Bind(inputService, "Commands/StepSimulation", AppCommands.Step);
            Bind(inputService, "Commands/SpeedUp", () => AppCommands.ChangeSpeed(2));
            Bind(inputService, "Commands/SpeedDown", () => AppCommands.ChangeSpeed(0.5));
            Bind(inputService, "Commands/CancelCommand", Cancel);
            Bind(inputService, "Commands/HelpCommand", AppCommands.ShowHelp);
        }

        private void Cancel()
        {
            if (AppUi.Instance != null && AppUi.Instance.CloseTopPanel())
                return;
            if (_wireAction.IsDrawing)
            {
                _wireAction.Cancel();
                return;
            }
            SystemCore.SelectingService.DeselectAll();
        }

        private static void Bind(InputService inputService, string action, Action handler)
        {
            var inputAction = inputService.Find(action);
            if (inputAction == null)
                return;
            inputAction.performed += _ =>
            {
                // Typing into a text field must not trigger editor shortcuts.
                if (EditCommands.IsTextInputFocused())
                    return;
                handler();
            };
        }
    }
}
