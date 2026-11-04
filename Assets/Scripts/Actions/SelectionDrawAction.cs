using Core.Input;
using Core.Selecting;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Actions
{
    public class SelectionDrawAction
    {
        private InputService _inputService;
        private SelectingService _selectingService;
        private DrawingSelectionManager _selectingManager;
        private Camera _camera;

        private Vector2 _mouseStartPosition;
        private Vector2 _mousePosition;
        private Vector2 _pendingPosition;

        private bool _isDrawingSelection = false;
        private bool _isDrawingSelectionPending = false;
        private bool _isDrawingSelectionEndPending = false;
        private bool _isDrawingSelectionMoreEndPending = false;

        public SelectionDrawAction(InputService inputService, ActionsManager actionsManager, SelectingService selectingService, DrawingSelectionManager drawingSelectionManager) 
        {
            _inputService = inputService;
            _selectingService = selectingService;
            _selectingManager = drawingSelectionManager;  
            _camera = Camera.main;

            _inputService.CurrentInputActions.FindAction("SelectionDraw").performed += OnSelectionDraw;
            _inputService.CurrentInputActions.FindAction("SelectionDraw").started += OnSelectionDrawStarted;
            _inputService.CurrentInputActions.FindAction("SelectionDraw").canceled += OnSelectionDrawEnd;
            _inputService.CurrentInputActions.FindAction("SelectionDrawMore").canceled += OnSelectingMoreEnd;

            actionsManager.UpdateEvent += Update;
        }

        private void Update()
        {
            if (_isDrawingSelectionPending)
            {
                // Only on empty canvas, and only while editing.
                if (Core.SystemCore.AppStateService.IsEditing && Physics2D.OverlapPoint(_pendingPosition) == null && !PointerQuery.IsOverUi())
                {
                    _mouseStartPosition = _pendingPosition;
                    _isDrawingSelection = true;
                    _selectingManager.EnableSelectionDrawer(true);
                }
            }

            if (_isDrawingSelection)
            {
                _selectingManager.DrawSelection(_mouseStartPosition, _mousePosition);
            }

            if (_isDrawingSelectionMoreEndPending)
            {
                SelectArea(_mouseStartPosition, _mousePosition);
                _isDrawingSelection = false;
            }
            else if (_isDrawingSelectionEndPending)
            {
                _selectingService.DeselectAll();
                SelectArea(_mouseStartPosition, _mousePosition);
                _isDrawingSelection = false;
            }

            _isDrawingSelectionPending = false;
            _isDrawingSelectionEndPending = false;
            _isDrawingSelectionMoreEndPending = false;
        }

        private void OnSelectionDrawStarted(InputAction.CallbackContext context)
        {
            if (_isDrawingSelection)
                return;

            _mousePosition = _camera.ScreenToWorldPoint(context.ReadValue<Vector2>());
            _pendingPosition = _mousePosition;

            _isDrawingSelectionPending = true;
        }

        private void OnSelectionDraw(InputAction.CallbackContext context)
        {
            _mousePosition = _camera.ScreenToWorldPoint(context.ReadValue<Vector2>());
        }

        private void OnSelectionDrawEnd(InputAction.CallbackContext context)
        {
            if (!_isDrawingSelection)
                return;
            _isDrawingSelectionEndPending = true;
            _selectingManager.EnableSelectionDrawer(false);
        }

        private void OnSelectingMoreEnd(InputAction.CallbackContext context)
        {
            if (!_isDrawingSelection)
                return;

            _isDrawingSelectionMoreEndPending = true;
            _selectingManager.EnableSelectionDrawer(false);
        }

        private void SelectArea(Vector2 startPosition, Vector2 endPosition)
        {
            var hits = Physics2D.OverlapAreaAll(startPosition, endPosition);

            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].TryGetComponent<ISelectable>(out ISelectable selectable))
                {
                    _selectingService.Select(selectable);
                }
            }
        }
    }
}
