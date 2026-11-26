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
            _inputService.Actions.GeneralGestures.SelectOne.performed += OnSelect;
            // Shift + mouse left click
            _inputService.Actions.GeneralGestures.SelectMany.performed += OnSelectMore;

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
            // Clicks on the UI (inspector, tool panel, dialogs) belong to it, not to the canvas behind it: they must
            // not deselect, or the inspector closes under the pointer.
            if (!Core.SystemCore.AppStateService.IsEditing || PointerQuery.IsOverUi())
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
                    if (!_selectingService.Contains(selectable))
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

        private ISelectable RayCast() => PointerQuery.SelectableAt(_pendingPosition);
    }
}
