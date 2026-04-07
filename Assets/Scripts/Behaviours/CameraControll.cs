using Core.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using Core;

namespace Behaviours
{
    [RequireComponent(typeof(Camera))]
    public class CameraControll : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private Transform _tiledBgTransform;
        private Vector2 _cameraMinMaxZoom;

        private Camera _mainCamera;
        private float _defaultZoom;

        private Vector3 _currentPosition;
        private Vector3 _deltaPosition;
        private Vector3 _lastPosition;

        private Vector2 _worldUnitsInCamera;
        private Vector2 _worldToPixelAmount;

        void Start()
        {
            SettingsService.SettingsChanged += OnSettingsChange;
            OnSettingsChange();
            _mainCamera = GetComponent<Camera>();
            PixelToWorldPoint();

            _tiledBgTransform.position = new Vector3(Mathf.Round(this.transform.position.x), Mathf.Round(this.transform.position.y), 5);

            var panAction = SystemCore.InputService.CurrentInputActions.FindAction("CameraPan");
            panAction.started += OnMoveStart;
            panAction.performed += OnMove;
            SystemCore.InputService.CurrentInputActions.FindAction("CameraZoom").performed += OnZoom;
        }

        private void OnSettingsChange()
        {
            _cameraMinMaxZoom = SettingsService.Settings.CameraMinMaxZoom;
            _defaultZoom = SettingsService.Settings.CameraOrthographicSize;
        }

        private void OnZoom(InputAction.CallbackContext context)
        {
            var mouseScroll = context.ReadValue<Vector2>();

            if (mouseScroll.y == 0)
                return;

            PixelToWorldPoint();
            _mainCamera.orthographicSize = Mathf.Clamp(_mainCamera.orthographicSize - mouseScroll.y, _cameraMinMaxZoom.x, _cameraMinMaxZoom.y);

            float sizeX = Mathf.Round(Screen.width / _worldToPixelAmount.x);
            float sizeY = Mathf.Round(Screen.height / _worldToPixelAmount.y);

            if (sizeX % 2 != 0)
            {
                sizeX++;
            }
            if (sizeY % 2 != 0)
            {
                sizeY++;
            }
            _spriteRenderer.size = new Vector3(sizeX + 10, sizeY + 10);
        }

        private void OnMoveStart(InputAction.CallbackContext context)
        {
            var mousePosition = context.ReadValue<Vector2>();
            _lastPosition = new Vector3(mousePosition.x / _worldToPixelAmount.x * -1, mousePosition.y / _worldToPixelAmount.y * -1);
        }

        private void OnMove(InputAction.CallbackContext context)
        {
            var mousePosition = context.ReadValue<Vector2>();
            _currentPosition = new Vector3(mousePosition.x / _worldToPixelAmount.x * -1, mousePosition.y / _worldToPixelAmount.y * -1);
            _deltaPosition = _currentPosition - _lastPosition;
            _lastPosition = _currentPosition;
            transform.position += _deltaPosition;
            _tiledBgTransform.position = new Vector3(Mathf.Round(this.transform.position.x), Mathf.Round(this.transform.position.y), 5);
         
        }

        private void PixelToWorldPoint()
        {
            _worldUnitsInCamera.y = _mainCamera.orthographicSize * 2;
            _worldUnitsInCamera.x = _worldUnitsInCamera.y * Screen.width / Screen.height;
            _worldToPixelAmount.x = Screen.width / _worldUnitsInCamera.x;
            _worldToPixelAmount.y = Screen.height / _worldUnitsInCamera.y;
        }
    }
}

