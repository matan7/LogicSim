using Core;
using Core.Input;
using LogicSim.Simulation;
using LogicSim.Views;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Actions
{
    /// <summary>
    /// Draws wires: press on a pin, drag to a pin of the opposite direction, release. Dropping on an input that is
    /// already driven rewires it; dragging a connected input onto empty canvas disconnects it.
    /// </summary>
    public class WireAction
    {
        private readonly Camera _camera;
        private readonly LineRenderer _preview;
        private readonly Vector3[] _points = new Vector3[6];

        private bool _dragging;
        private bool _pending;
        private Vector2 _pendingPosition;
        private Vector2 _pointerWorld;
        private PinView _startPin;
        private PinView _hoverPin;

        public bool IsDrawing => _startPin != null;

        public WireAction(InputService inputService, ActionsManager actionsManager)
        {
            _camera = Camera.main;
            var drag = inputService.Find("Drag");
            if (drag != null)
            {
                drag.performed += OnDrag;
                drag.canceled += OnRelease;
            }
            actionsManager.UpdateEvent += Update;

            var go = new GameObject("Wire Preview");
            Object.DontDestroyOnLoad(go);
            _preview = go.AddComponent<LineRenderer>();
            WireView.ConfigureLine(_preview, VisualStyle.PreviewSortingOrder);
            _preview.enabled = false;
        }

        private void OnDrag(InputAction.CallbackContext context)
        {
            _pointerWorld = _camera.ScreenToWorldPoint(context.ReadValue<Vector2>());
            if (!_dragging)
            {
                _dragging = true;
                _pending = true;
                _pendingPosition = _pointerWorld;
            }
        }

        private void Update()
        {
            if (_pending)
            {
                _pending = false;
                if (SystemCore.AppStateService.IsEditing && !PointerQuery.IsOverUi())
                {
                    var pin = PointerQuery.PinAt(_pendingPosition);
                    if (pin != null)
                        Begin(pin);
                }
            }

            if (_startPin == null)
                return;
            if (_startPin.Owner == null)
            {
                Cancel();
                return;
            }

            var hover = PointerQuery.PinAt(_pointerWorld);
            if (hover != null && (hover == _startPin || hover.IsOutput == _startPin.IsOutput))
                hover = null;
            if (hover != _hoverPin)
            {
                if (_hoverPin != null)
                    _hoverPin.SetHighlighted(false);
                _hoverPin = hover;
                if (_hoverPin != null)
                    _hoverPin.SetHighlighted(true);
            }

            Vector3 end = _hoverPin != null ? _hoverPin.transform.position : (Vector3)_pointerWorld;
            Vector3 from = _startPin.IsOutput ? _startPin.transform.position : end;
            Vector3 to = _startPin.IsOutput ? end : _startPin.transform.position;
            int count = WireView.Route(from, to, _points);
            _preview.positionCount = count;
            for (int i = 0; i < count; i++)
                _preview.SetPosition(i, _points[i]);
            Color color = _hoverPin != null ? VisualStyle.PinHighlight : VisualStyle.WireColor;
            _preview.startColor = color;
            _preview.endColor = color;
        }

        private void Begin(PinView pin)
        {
            _startPin = pin;
            _startPin.SetHighlighted(true);
            _preview.enabled = true;
        }

        private void OnRelease(InputAction.CallbackContext context)
        {
            _dragging = false;
            _pending = false;
            if (_startPin == null)
                return;

            var start = _startPin;
            var target = _hoverPin;
            Cancel();
            if (start.Owner == null)
                return;

            var circuit = SystemCore.CircuitService.Circuit;
            try
            {
                if (target != null && target.Owner != null)
                {
                    var output = start.IsOutput ? start : target;
                    var input = start.IsOutput ? target : start;
                    var existing = circuit.GetWireInto(input.Owner.ComponentId, input.Index);
                    if (existing != null && existing.From.Equals(output.PinRef))
                        return;
                    SystemCore.CircuitService.Editor.Apply(new ConnectEdit(circuit, output.PinRef, input.PinRef));
                }
                else if (!start.IsOutput && Vector2.Distance(_pointerWorld, start.transform.position) > 1f)
                {
                    var existing = circuit.GetWireInto(start.Owner.ComponentId, start.Index);
                    if (existing != null)
                        SystemCore.CircuitService.Editor.Apply(new RemoveElementsEdit(circuit, System.Array.Empty<int>(), new[] { existing.Id }));
                }
            }
            catch (CircuitException exception)
            {
                Debug.LogWarning("[LogicSim] " + exception.Message);
            }
        }

        public void Cancel()
        {
            if (_startPin != null)
                _startPin.SetHighlighted(false);
            if (_hoverPin != null)
                _hoverPin.SetHighlighted(false);
            _startPin = null;
            _hoverPin = null;
            _preview.enabled = false;
        }
    }
}
