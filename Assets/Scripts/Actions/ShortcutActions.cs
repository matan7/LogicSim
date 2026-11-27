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
            Bind(inputService.Actions.Commands.CutCommand, () => EditCommands.Cut());
            Bind(inputService.Actions.Commands.SelectAllCommand, EditCommands.SelectAll);
            Bind(inputService.Actions.Commands.RotateCommand, () =>
            {
                // R is also part of Ctrl+R (redo).
                if (Keyboard.current != null && Keyboard.current.ctrlKey.isPressed)
                    return;
                EditCommands.Rotate();
            });
            Bind(inputService.Actions.Commands.SaveCommand, () => AppCommands.Save(saveAs: false));
            Bind(inputService.Actions.Commands.OpenCommand, AppCommands.ShowOpen);
            Bind(inputService.Actions.Commands.NewCommand, AppCommands.New);
            Bind(inputService.Actions.Commands.ToggleSimulation, AppCommands.ToggleSimulation);
            Bind(inputService.Actions.Commands.PauseSimulation, AppCommands.TogglePause);
            Bind(inputService.Actions.Commands.StepSimulation, AppCommands.Step);
            Bind(inputService.Actions.Commands.SpeedUp, () => AppCommands.ChangeSpeed(2));
            Bind(inputService.Actions.Commands.SpeedDown, () => AppCommands.ChangeSpeed(0.5));
            Bind(inputService.Actions.Commands.CancelCommand, Cancel);
            Bind(inputService.Actions.Commands.HelpCommand, AppCommands.ShowHelp);
            Bind(inputService.Actions.Commands.FrameAll, AppCommands.FrameAll);
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
            // Esc first deselects, then leaves the open IC (keeping its changes).
            if (SystemCore.SelectingService.Count == 0 && !SystemCore.CircuitService.Hierarchy.IsAtRoot)
            {
                AppCommands.CloseSubcircuit(apply: true);
                return;
            }
            SystemCore.SelectingService.DeselectAll();
        }

        private static void Bind(InputAction inputAction, Action handler)
        {
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
