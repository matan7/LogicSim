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
        private Dragable _dragable;
        private UiDragable _uIDragable;
        private CommandService _commandService;
        private SelectingService _selectingService;

        // Fields
        private bool _isDragging;
        private RaycastHit2D _hit;
        private Vector3 _startPosition;
        private bool _isDragPending;
        private Vector2 _pendingPosition;
        private Vector2 _mousePosition;
        private List<Dragable> _selectedDragables = new List<Dragable>();
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
                    if (results.Count > 0 && results[0].gameObject.TryGetComponent<UiDragable>(out UiDragable dragable))
                    {
                        _uIDragable = dragable;
                    }
                }
                else
                {
                    _hit = Physics2D.Raycast(_pendingPosition, Vector2.zero);
                    if (_hit.collider != null && _hit.collider.TryGetComponent<Dragable>(out Dragable dragable))
                    {
                        var selectionList = _selectingService.SelectionList;
                        if (selectionList.Count > 1 && dragable.TryGetComponent<ISelectable>(out ISelectable selectable) && selectionList.Contains(selectable))
                        {
                            _selectedDragables.Clear();
                            _startPositions.Clear();

                            for (int i = 0; i < selectionList.Count; i++)
                            {
                                if (!selectionList[i].Transform.TryGetComponent<Dragable>(out Dragable otherDragable))
                                    continue;

                                _selectedDragables.Add(otherDragable);
                                _startPositions.Add(otherDragable.OnStartDrag(_pendingPosition));
                            }
                        }
                        else
                        {
                            _dragable = dragable;
                            _startPosition = _dragable.OnStartDrag(_pendingPosition);
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

            if (_selectedDragables.Count > 1)
            {
                List<Vector3> endPositions = new List<Vector3>();
                for (int i = 0; i < _selectedDragables.Count; i++)
                    endPositions.Add(_selectedDragables[i].OnDragEnd());

                if (Vector3.Distance(_startPositions[0], endPositions[0]) > SettingsService.Settings.DragThreshold)
                {
                    SelectionDragCommand command = new SelectionDragCommand(new List<Dragable>(_selectedDragables), new List<Vector3>(_startPositions), endPositions);
                    _commandService.ExecuteCommand(command);
                }
                else
                {
                    for (int i = 0; i < _selectedDragables.Count; i++)
                        _selectedDragables[i].transform.position = _startPositions[i];
                }
            }
            else if (_dragable != null)
            {
                Vector3 endPosition = _dragable.OnDragEnd();

                if (Vector3.Distance(_startPosition, endPosition) > SettingsService.Settings.DragThreshold)
                {
                    DragCommand command = new DragCommand(_dragable, _startPosition, endPosition);
                    _commandService.ExecuteCommand(command);
                }
                else
                {
                    _dragable.transform.position = _startPosition;
                }

                _dragable = null;
            }

            if (_uIDragable != null)
            {
                _uIDragable.OnDragEnd();
                _uIDragable = null;
            }

            _selectedDragables.Clear();
            _startPositions.Clear();
        }


        private void Drag(Vector2 mousePosition)
        {
            if (_uIDragable != null)
            {
                _uIDragable.OnDrag(_mousePosition);
            }
            if (_selectedDragables.Count > 1) 
            {
                for (int i = 0; i < _selectedDragables.Count; i++)
                {
                    _selectedDragables[i].OnDrag(mousePosition);
                }
            }
            else if (_dragable != null)
            {
                _dragable.OnDrag(mousePosition);
            }
        }
    }
}