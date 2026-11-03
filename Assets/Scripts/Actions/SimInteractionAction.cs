using Core;
using Core.Input;
using LogicSim.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Actions
{
    /// <summary>During the simulation: click switches and input pins to toggle them, hold buttons.</summary>
    public class SimInteractionAction
    {
        private readonly Camera _camera;
        private bool _pressed;
        private bool _pending;
        private Vector2 _pendingPosition;
        private int _heldButton;

        public SimInteractionAction(InputService inputService, ActionsManager actionsManager)
        {
            _camera = Camera.main;
            var drag = inputService.Find("Drag");
            if (drag != null)
            {
                drag.performed += OnPointer;
                drag.canceled += OnRelease;
            }
            actionsManager.UpdateEvent += Update;
        }

        private void OnPointer(InputAction.CallbackContext context)
        {
            if (_pressed)
                return;
            _pressed = true;
            _pending = true;
            _pendingPosition = _camera.ScreenToWorldPoint(context.ReadValue<Vector2>());
        }

        private void Update()
        {
            if (!_pending)
                return;
            _pending = false;
            if (!SystemCore.AppStateService.IsSimulating || PointerQuery.IsOverUi())
                return;

            var view = PointerQuery.ComponentAt(_pendingPosition);
            if (view == null)
            {
                var pin = PointerQuery.PinAt(_pendingPosition);
                view = pin != null ? pin.Owner : null;
            }
            if (view == null)
                return;

            var circuit = SystemCore.CircuitService.Circuit;
            if (!circuit.TryGetComponent(view.ComponentId, out var component))
                return;
            switch (component.Kind)
            {
                case ComponentKind.Switch:
                case ComponentKind.PinIn:
                    circuit.SetInputValue(component.Id, !component.Value);
                    break;
                case ComponentKind.Button:
                    circuit.SetInputValue(component.Id, true);
                    _heldButton = component.Id;
                    break;
            }
        }

        private void OnRelease(InputAction.CallbackContext context)
        {
            _pressed = false;
            _pending = false;
            if (_heldButton == 0)
                return;
            var circuit = SystemCore.CircuitService.Circuit;
            if (circuit.TryGetComponent(_heldButton, out _))
                circuit.SetInputValue(_heldButton, false);
            _heldButton = 0;
        }
    }
}
