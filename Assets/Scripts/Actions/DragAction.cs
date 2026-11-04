using System.Collections.Generic;
using Behaviours;
using Core;
using Core.Input;
using Core.Selecting;
using Core.Settings;
using LogicSim.Simulation;
using LogicSim.Views;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Actions
{
    /// <summary>Drags components (or the whole selection) and palette entries. Moves become one undoable edit.</summary>
    public class DragAction
    {
        private readonly InputService _inputService;
        private readonly SelectingService _selectingService;
        private readonly Camera _camera;

        private bool _isDragging;
        private bool _isDragPending;
        private Vector2 _pendingPosition;
        private Vector2 _mousePosition;
        private UiDraggable _uiDraggable;
        private readonly List<Draggable> _draggables = new List<Draggable>();
        private readonly List<int> _componentIds = new List<int>();
        private readonly List<Vector3> _startPositions = new List<Vector3>();

        public DragAction(InputService inputService, ActionsManager actionsManager, SelectingService selectingService)
        {
            _inputService = inputService;
            _selectingService = selectingService;
            _camera = Camera.main;

            var drag = _inputService.Find("Drag");
            if (drag != null)
            {
                drag.performed += OnDrag;
                drag.canceled += OnEndDrag;
            }
            actionsManager.UpdateEvent += Update;
        }

        private void Update()
        {
            if (!_isDragPending)
                return;
            _isDragPending = false;
            if (!SystemCore.AppStateService.IsEditing)
                return;

            if (PointerQuery.IsOverUi())
            {
                var ped = new PointerEventData(EventSystem.current) { position = _mousePosition };
                var results = new List<RaycastResult>();
                EventSystem.current.RaycastAll(ped, results);
                if (results.Count > 0 && results[0].gameObject.TryGetComponent(out UiDraggable draggable))
                    _uiDraggable = draggable;
                return;
            }

            // Pins start wires (WireAction); only bodies are dragged.
            if (PointerQuery.PinAt(_pendingPosition) != null)
                return;
            var view = PointerQuery.ComponentAt(_pendingPosition);
            if (view == null || !view.TryGetComponent(out Draggable pressed))
                return;

            _draggables.Clear();
            _componentIds.Clear();
            _startPositions.Clear();

            var selectionList = _selectingService.SelectionList;
            bool dragSelection = view.TryGetComponent(out ISelectable selectable) && _selectingService.Contains(selectable) && selectionList.Count > 1;
            if (dragSelection)
            {
                foreach (var item in selectionList)
                {
                    if (item.Transform == null || !item.Transform.TryGetComponent(out Draggable other) || !item.Transform.TryGetComponent(out ComponentView otherView))
                        continue;
                    Add(other, otherView);
                }
            }
            else
            {
                Add(pressed, view);
            }
        }

        private void Add(Draggable draggable, ComponentView view)
        {
            _draggables.Add(draggable);
            _componentIds.Add(view.ComponentId);
            _startPositions.Add(draggable.OnStartDrag(_pendingPosition));
        }

        private void OnDrag(InputAction.CallbackContext context)
        {
            _mousePosition = context.ReadValue<Vector2>();
            Vector2 world = _camera.ScreenToWorldPoint(_mousePosition);

            if (!_isDragging)
            {
                _isDragging = true;
                _isDragPending = true;
                _pendingPosition = world;
            }

            if (_uiDraggable != null)
                _uiDraggable.OnDrag(_mousePosition);
            for (int i = 0; i < _draggables.Count; i++)
                if (_draggables[i] != null)
                    _draggables[i].OnDrag(world);
        }

        private void OnEndDrag(InputAction.CallbackContext context)
        {
            _isDragging = false;
            _isDragPending = false;

            if (_draggables.Count > 0)
                FinishMove();

            if (_uiDraggable != null)
            {
                _uiDraggable.OnDragEnd();
                _uiDraggable = null;
            }
        }

        private void FinishMove()
        {
            var circuit = SystemCore.CircuitService.Circuit;
            var ids = new List<int>();
            var from = new List<(float x, float y)>();
            var to = new List<(float x, float y)>();
            bool moved = false;
            for (int i = 0; i < _draggables.Count; i++)
            {
                if (_draggables[i] == null || !circuit.TryGetComponent(_componentIds[i], out _))
                    continue;
                Vector3 end = _draggables[i].OnDragEnd();
                ids.Add(_componentIds[i]);
                from.Add((_startPositions[i].x, _startPositions[i].y));
                to.Add((end.x, end.y));
                moved |= Vector3.Distance(_startPositions[i], end) > SettingsService.Settings.DragThreshold;
            }

            if (moved)
            {
                SystemCore.CircuitService.Editor.Apply(new MoveComponentsEdit(circuit, ids, from, to));
            }
            else
            {
                // A click, not a drag: put everything back exactly.
                for (int i = 0; i < _draggables.Count; i++)
                    if (_draggables[i] != null)
                        _draggables[i].transform.position = _startPositions[i];
            }

            _draggables.Clear();
            _componentIds.Clear();
            _startPositions.Clear();
        }
    }
}
