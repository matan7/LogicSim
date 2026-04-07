using Core;
using Behaviours;
using Core.Command;
using Commands;
using Core.Input;
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

        // Fields
        private bool _isDragging;
        private RaycastHit2D _hit;
        private Vector3 _startPosition;
        private bool _isDragPending;
        private Vector2 _pendingPosition;
        private Vector2 _mousePosition;

        public DragAction(InputService inputService, CommandService commandService, ActionsManager actionsManager)
        {
            _inputService = inputService;
            _commandService = commandService;
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
                        _dragable = dragable;
                        _startPosition = _dragable.OnStartDrag(_pendingPosition);
                    }
                }
            }
        }

        private void OnDrag(InputAction.CallbackContext context)
        {
            _mousePosition = context.ReadValue<Vector2>();
            Vector2 mousePosition = _camera.ScreenToWorldPoint(_mousePosition);
            StartDrag(mousePosition);
            Drag(mousePosition);
        }

        private void OnEndDrag(InputAction.CallbackContext context)
        {
            _isDragging = false;
            if (_dragable != null)
            {
                Vector3 endPosition = _dragable.OnDragEnd();

                DragCommand command = new DragCommand(_dragable, _startPosition, endPosition);
                _commandService.ExecuteCommand(command);
                _dragable = null;
            }
            if (_uIDragable != null)
            {
                _uIDragable.OnDragEnd();
                _uIDragable = null;
            }
        }

        private void StartDrag(Vector2 mousePosition)
        {
            if (_isDragging)
            {
                return;
            }

            _isDragging = true;

            _isDragPending = true;
            _pendingPosition = mousePosition;
        }

        private void Drag(Vector2 mousePosition)
        {
            if (_dragable != null)
            {
                _dragable.OnDrag(mousePosition);
            }
            if (_uIDragable != null)
            {
                _uIDragable.OnDrag(_mousePosition);
            }
        }
    }
}