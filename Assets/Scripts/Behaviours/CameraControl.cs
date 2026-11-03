using Core;
using Core.Settings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Behaviours
{
    /// <summary>Pan (middle mouse or Alt + left mouse), zoom towards the cursor, and framing of content.</summary>
    [RequireComponent(typeof(Camera))]
    public class CameraControl : MonoBehaviour
    {
        public static CameraControl Instance { get; private set; }

        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private Transform _tiledBgTransform;

        private Vector2 _cameraMinMaxZoom;
        private Camera _mainCamera;
        private Vector2 _lastPointer;

        public Camera Camera => _mainCamera;

        private void Awake()
        {
            Instance = this;
            _mainCamera = GetComponent<Camera>();
        }

        private void Start()
        {
            SettingsService.SettingsChanged += OnSettingsChange;
            OnSettingsChange();
            UpdateBackground();

            var panAction = SystemCore.InputService.Find("CameraPan");
            if (panAction != null)
            {
                panAction.started += OnMoveStart;
                panAction.performed += OnMove;
            }
            var zoomAction = SystemCore.InputService.Find("CameraZoom");
            if (zoomAction != null)
                zoomAction.performed += OnZoom;
        }

        private void OnDestroy()
        {
            SettingsService.SettingsChanged -= OnSettingsChange;
            if (Instance == this)
                Instance = null;
        }

        private void OnSettingsChange()
        {
            _cameraMinMaxZoom = SettingsService.Settings.CameraMinMaxZoom;
        }

        private void OnZoom(InputAction.CallbackContext context)
        {
            var scroll = context.ReadValue<Vector2>();
            if (scroll.y == 0)
                return;
            // Scrolling over the tool panel scrolls the panel instead.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            var pointer = Pointer.current != null ? Pointer.current.position.ReadValue() : new Vector2(Screen.width / 2f, Screen.height / 2f);
            Vector3 before = _mainCamera.ScreenToWorldPoint(pointer);
            float step = Mathf.Sign(scroll.y) * Mathf.Max(1f, _mainCamera.orthographicSize * 0.1f);
            _mainCamera.orthographicSize = Mathf.Clamp(_mainCamera.orthographicSize - step, _cameraMinMaxZoom.x, _cameraMinMaxZoom.y);
            Vector3 after = _mainCamera.ScreenToWorldPoint(pointer);
            transform.position += new Vector3(before.x - after.x, before.y - after.y, 0);
            UpdateBackground();
        }

        private void OnMoveStart(InputAction.CallbackContext context)
        {
            _lastPointer = context.ReadValue<Vector2>();
        }

        private void OnMove(InputAction.CallbackContext context)
        {
            var pointer = context.ReadValue<Vector2>();
            Vector3 previous = _mainCamera.ScreenToWorldPoint(_lastPointer);
            Vector3 current = _mainCamera.ScreenToWorldPoint(pointer);
            _lastPointer = pointer;
            transform.position += new Vector3(previous.x - current.x, previous.y - current.y, 0);
            UpdateBackground();
        }

        /// <summary>Centers the view on <paramref name="bounds"/> and zooms out if needed.</summary>
        public void FrameBounds(Bounds bounds, bool onlyIfOutside)
        {
            float halfHeight = _mainCamera.orthographicSize;
            float halfWidth = halfHeight * _mainCamera.aspect;
            var center = transform.position;
            bool inside = bounds.min.x >= center.x - halfWidth && bounds.max.x <= center.x + halfWidth
                && bounds.min.y >= center.y - halfHeight && bounds.max.y <= center.y + halfHeight;
            if (onlyIfOutside && inside)
                return;

            const float margin = 4f;
            float needed = Mathf.Max(bounds.extents.y + margin, (bounds.extents.x + margin) / Mathf.Max(0.1f, _mainCamera.aspect));
            _mainCamera.orthographicSize = Mathf.Clamp(Mathf.Max(needed, onlyIfOutside ? halfHeight : _cameraMinMaxZoom.x), _cameraMinMaxZoom.x, _cameraMinMaxZoom.y);
            transform.position = new Vector3(bounds.center.x, bounds.center.y, transform.position.z);
            UpdateBackground();
        }

        private void UpdateBackground()
        {
            if (_tiledBgTransform != null)
                _tiledBgTransform.position = new Vector3(Mathf.Round(transform.position.x), Mathf.Round(transform.position.y), 5);
            if (_spriteRenderer != null)
            {
                float height = Mathf.Ceil(_mainCamera.orthographicSize * 2) + 10;
                float width = Mathf.Ceil(height * _mainCamera.aspect) + 10;
                // Even sizes keep the tiled grid aligned with integer coordinates.
                _spriteRenderer.size = new Vector2(width + width % 2, height + height % 2);
            }
        }
    }
}
