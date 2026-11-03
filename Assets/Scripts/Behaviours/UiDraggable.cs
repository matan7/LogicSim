using Core.Settings;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Behaviours
{
    [RequireComponent(typeof(RectTransform))]
    public class UiDraggable : MonoBehaviour
    {
        public event Action<Vector3> EndDrag;

        private Image _image;

        private RectTransform _rectTransform;
        private Vector3 _startPosition;
        private Vector2 _startSize;
        private Camera _mainCamera;
        private Color _startColor;
        private Vector2 _dropPositionOffset;
        private Vector2 _orthographicSizeFactor;
        private Color _dragColor;
        private Transform _originalParent;
        private int _originalSiblingIndex;
        private bool _dragging;

        void Start()
        {
            _image = GetComponent<Image>();

            _rectTransform = gameObject.GetComponent<RectTransform>();
            _startPosition = transform.localPosition;
            _mainCamera = Camera.main;
            _startColor = _image.color;
            _startSize = _rectTransform.sizeDelta;

            SettingsService.SettingsChanged += OnServiceChanged;

            OnServiceChanged();
        }

        private void OnServiceChanged()
        {
            _dropPositionOffset = SettingsService.Settings.DropPositionOffset;
            _orthographicSizeFactor = SettingsService.Settings.OrthographicSizeFactor;
            _dragColor = SettingsService.Settings.DragColor;
        }

        internal void OnDrag(Vector2 mousePosition)
        {
            if (!_dragging)
            {
                // Lift the icon onto the root canvas so the scrolling tool panel does not clip it.
                _dragging = true;
                _originalParent = transform.parent;
                _originalSiblingIndex = transform.GetSiblingIndex();
                var canvas = GetComponentInParent<Canvas>();
                if (canvas != null)
                    transform.SetParent(canvas.rootCanvas.transform, true);
            }
            _rectTransform.sizeDelta = new Vector2(1 / _mainCamera.orthographicSize * _orthographicSizeFactor.x, 1 / _mainCamera.orthographicSize * _orthographicSizeFactor.y);
            transform.position = mousePosition;
            _image.color = _dragColor;
        }

        internal void OnDragEnd()
        {
            var world = _mainCamera.ScreenToWorldPoint(transform.position);
            if (_dragging && _originalParent != null)
            {
                transform.SetParent(_originalParent, false);
                transform.SetSiblingIndex(_originalSiblingIndex);
            }
            _dragging = false;
            _rectTransform.sizeDelta = _startSize;
            transform.localPosition = _startPosition;
            _image.color = _startColor;
            EndDrag?.Invoke(new Vector3(world.x - _dropPositionOffset.x, world.y + _dropPositionOffset.y));
        }
    }
}
