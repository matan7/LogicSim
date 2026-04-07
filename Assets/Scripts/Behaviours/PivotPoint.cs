using Commands;
using Core;
using Core.Settings;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Behaviours
{
    public class PivotPoint : MonoBehaviour
    {
        private WaitForSeconds _waiting;
        private Coroutine _clickTimer;
        private Camera _camera;
        private bool _isClickStarted = false;
        private Vector2 _lastMousePosition;
        private Vector2 _startPosition;

        private void Start()
        {
            _waiting = new WaitForSeconds(SettingsService.Settings.ClickGestureTimeMax);
            _camera = Camera.main;
            SettingsService.SettingsChanged += OnSettingsChanged;
            var inputAction = SystemCore.InputService.CurrentInputActions.FindAction("PivotPositionChange");
            inputAction.performed += OnMouseRightClickStart;
            inputAction.canceled += OnMouseRightClickEnd;
         
        }

        private void OnSettingsChanged()
        {
            _waiting = new WaitForSeconds(SettingsService.Settings.ClickGestureTimeMax);
        }

        private void OnMouseRightClickStart(InputAction.CallbackContext context)
        {
            _isClickStarted = true;
            _lastMousePosition = context.ReadValue<Vector2>();
            _startPosition = transform.position;
            _clickTimer = StartCoroutine(ClickTimer());
        }

        private void OnMouseRightClickEnd(InputAction.CallbackContext context)
        {
            if (!_isClickStarted)
                return;
            
            var position = _camera.ScreenToWorldPoint(_lastMousePosition);
            transform.position = position;
            if (SettingsService.Settings.SnapToGrid)
                transform.position = new(Mathf.Round(transform.position.x), Mathf.Round(transform.position.y));

            PivotMoveCommand command = new PivotMoveCommand(transform, _startPosition, transform.position);
            SystemCore.CommandService.ExecuteCommand(command);

            if (_clickTimer != null)
            {
                StopCoroutine(_clickTimer);
            }
            _isClickStarted = false;
        }

        private IEnumerator ClickTimer()
        {
            yield return _waiting;
            _isClickStarted = false;
        }
    }

}
