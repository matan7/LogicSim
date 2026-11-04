using Core.Input;
using Core.Selecting;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Actions
{
    public class SelectAction
    {
        private InputService _inputService;
        private SelectingService _selectingService;
        private Camera _camera;

        private bool _isSelectPending = false;
        private bool _isSelectMorePending = false;

        private Vector2 _pendingPosition;

        public SelectAction(InputService inputService, ActionsManager actionsManager, SelectingService selectingService)
        {
            _inputService = inputService;
            _selectingService = selectingService;
            _camera = Camera.main;

            // Mouse left click
            _inputService.CurrentInputActions.FindAction("SelectOne").performed += OnSelect;
            // Shift + mouse left click
            _inputService.CurrentInputActions.FindAction("SelectMany").performed += OnSelectMore;

            actionsManager.UpdateEvent += Update;
        }
        private void OnSelect(InputAction.CallbackContext context)
        {
            _isSelectPending = true;
            _pendingPosition = _camera.ScreenToWorldPoint(Pointer.current.position.value);
        }

        private void OnSelectMore(InputAction.CallbackContext context)
        {
            _isSelectMorePending = true;
            _pendingPosition = _camera.ScreenToWorldPoint(Pointer.current.position.value);
        }

        private void Update()
        {
            if (!Core.SystemCore.AppStateService.IsEditing)
            {
                _isSelectMorePending = false;
                _isSelectPending = false;
                return;
            }
            if (_isSelectMorePending)
            {
                ISelectable selectable = RayCast();
                if (selectable != null)
                {
                    if (!_selectingService.SelectionList.Exists(s => ReferenceEquals(s, selectable)))
                    {
                        _selectingService.Select(selectable);
                    }
                    else
                    {
                        _selectingService.Deselect(selectable);
                    }
                }
            }
            else if (_isSelectPending)
            {
                ISelectable selectable = RayCast();
                _selectingService.DeselectAll();
                if (selectable != null)
                {
                    _selectingService.Select(selectable);
                }
            }
            _isSelectMorePending = false;
            _isSelectPending = false;
        }

        private ISelectable RayCast()
        {
            if (PointerQuery.IsOverUi())
                return null;
            return PointerQuery.SelectableAt(_pendingPosition);
        }
    }
}
