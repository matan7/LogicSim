using Behaviours;
using Core.Command;
using Commands;
using Core.Input;
using Core.Settings;
using Core.Selecting;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Actions
{
    public class DragAction
    {
        // References
        private InputService _inputService;
        private Camera _camera;
        private Draggable _draggable;
        private UiDraggable _uiDraggable;
        private CommandService _commandService;
        private SelectingService _selectingService;

        // Fields
        private bool _isDragging;
        private RaycastHit2D _hit;
        private Vector3 _startPosition;
        private bool _isDragPending;
        private Vector2 _pendingPosition;
        private Vector2 _mousePosition;
        private List<Draggable> _selectedDraggables = new List<Draggable>();
        private List<Vector3> _startPositions = new List<Vector3>();

        public DragAction(InputService inputService, CommandService commandService, ActionsManager actionsManager, SelectingService selectingService)
        {
            _inputService = inputService;
            _commandService = commandService;
            _selectingService = selectingService;

            _inputService.CurrentInputActions.FindAction("Drag").performed += OnDrag;
            _inputService.CurrentInputActions.FindAction("Drag").canceled += OnEndDrag;
            _camera = Camera.main;

            actionsManager.UpdateEvent += Update;
        }

        private void Update()
        {
            if (_isDragPending)
            {
                _isDragPending = false;

                if (EventSystem.current.IsPointerOverGameObject())
                {
                    var ped = new PointerEventData(EventSystem.current) { position = _mousePosition };
                    var results = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(ped, results);
                    if (results.Count > 0 && results[0].gameObject.TryGetComponent<UiDraggable>(out UiDraggable draggable))
                    {
                        _uiDraggable = draggable;
                    }
                }
                else
                {
                    _hit = Physics2D.Raycast(_pendingPosition, Vector2.zero);
                    if (_hit.collider != null && _hit.collider.TryGetComponent<Draggable>(out Draggable draggable))
                    {
                        var selectionList = _selectingService.SelectionList;
                        if (selectionList.Count > 1 && draggable.TryGetComponent<ISelectable>(out ISelectable selectable) && selectionList.Contains(selectable))
                        {
                            _selectedDraggables.Clear();
                            _startPositions.Clear();

                            for (int i = 0; i < selectionList.Count; i++)
                            {
                                if (!selectionList[i].Transform.TryGetComponent<Draggable>(out Draggable otherDraggable))
                                    continue;

                                _selectedDraggables.Add(otherDraggable);
                                _startPositions.Add(otherDraggable.OnStartDrag(_pendingPosition));
                            }
                        }
                        else
                        {
                            _draggable = draggable;
                            _startPosition = _draggable.OnStartDrag(_pendingPosition);
                        }
                    }
                }
            }
        }

        private void OnDrag(InputAction.CallbackContext context)
        {
            _mousePosition = context.ReadValue<Vector2>();
            Vector2 mousePosition = _camera.ScreenToWorldPoint(_mousePosition);

            if (!_isDragging)
            {
                _isDragging = true;
                _isDragPending = true;
                _pendingPosition = mousePosition;
            }

            Drag(mousePosition);
        }

        private void OnEndDrag(InputAction.CallbackContext context)
        {
            _isDragging = false;

            if (_selectedDraggables.Count > 1)
            {
                List<Vector3> endPositions = new List<Vector3>();
                for (int i = 0; i < _selectedDraggables.Count; i++)
                    endPositions.Add(_selectedDraggables[i].OnDragEnd());

                if (Vector3.Distance(_startPositions[0], endPositions[0]) > SettingsService.Settings.DragThreshold)
                {
                    SelectionDragCommand command = new SelectionDragCommand(new List<Draggable>(_selectedDraggables), new List<Vector3>(_startPositions), endPositions);
                    _commandService.ExecuteCommand(command);
                }
                else
                {
                    for (int i = 0; i < _selectedDraggables.Count; i++)
                        _selectedDraggables[i].transform.position = _startPositions[i];
                }
            }
            else if (_draggable != null)
            {
                Vector3 endPosition = _draggable.OnDragEnd();

                if (Vector3.Distance(_startPosition, endPosition) > SettingsService.Settings.DragThreshold)
                {
                    DragCommand command = new DragCommand(_draggable, _startPosition, endPosition);
                    _commandService.ExecuteCommand(command);
                }
                else
                {
                    _draggable.transform.position = _startPosition;
                }

                _draggable = null;
            }

            if (_uiDraggable != null)
            {
                _uiDraggable.OnDragEnd();
                _uiDraggable = null;
            }

            _selectedDraggables.Clear();
            _startPositions.Clear();
        }


        private void Drag(Vector2 mousePosition)
        {
            if (_uiDraggable != null)
            {
                _uiDraggable.OnDrag(_mousePosition);
            }
            if (_selectedDraggables.Count > 1) 
            {
                for (int i = 0; i < _selectedDraggables.Count; i++)
                {
                    _selectedDraggables[i].OnDrag(mousePosition);
                }
            }
            else if (_draggable != null)
            {
                _draggable.OnDrag(mousePosition);
            }
        }
    }
}