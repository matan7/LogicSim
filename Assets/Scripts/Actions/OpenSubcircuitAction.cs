using Core;
using Core.Input;
using LogicSim.Simulation;
using LogicSim.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Actions
{
    /// <summary>Double-click an IC to open it: for editing in edit mode, to see its live values while simulating.</summary>
    public class OpenSubcircuitAction
    {
        private const float DoubleClickSeconds = 0.35f;
        private const float MaxDistance = 0.6f;

        private readonly Camera _camera;
        private float _lastTime = -1;
        private Vector2 _lastPosition;
        private int _lastComponent;
        private bool _pending;
        private Vector2 _pendingPosition;

        public OpenSubcircuitAction(InputService inputService, ActionsManager actionsManager)
        {
            _camera = Camera.main;
            // "Drag" reports the pointer position while the button is held: the first report is the press.
            var drag = inputService.Find("Drag");
            if (drag != null)
            {
                drag.performed += OnPointer;
                drag.canceled += _ => _pressed = false;
            }
            actionsManager.UpdateEvent += Update;
        }

        private bool _pressed;

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
            if (PointerQuery.IsOverUi())
                return;

            var view = PointerQuery.ComponentAt(_pendingPosition);
            int id = view != null && view.Component != null && view.Component.Kind == ComponentKind.Subcircuit ? view.ComponentId : 0;
            float now = Time.unscaledTime;
            bool isDouble = id != 0 && id == _lastComponent && now - _lastTime <= DoubleClickSeconds
                && Vector2.Distance(_pendingPosition, _lastPosition) <= MaxDistance;
            _lastTime = now;
            _lastPosition = _pendingPosition;
            _lastComponent = id;
            if (!isDouble)
                return;
            _lastComponent = 0;
            AppCommands.OpenSubcircuit(id);
        }
    }
}
