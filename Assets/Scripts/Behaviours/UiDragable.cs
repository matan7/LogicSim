using Core.Settings;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Behaviours
{
    [RequireComponent(typeof(RectTransform))]
    public class UiDragable : MonoBehaviour
    {
        public event Action<Vector3> EndDrag;

        private Image _image;

        private RectTransform _rectTransform;
        private Vector3 _startPosition;
        private Vector2 _startSize;
        private Camera _mainCamera;
        private Color _startColor;
        private Vector2 _dropPositionOffset;
        private Vector2 _ortographicSizeFactor;
        private Color _dragColor;

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
            _ortographicSizeFactor = SettingsService.Settings.OrthographicSizeFactor;
            _dragColor = SettingsService.Settings.DragColor;
        }

        internal void OnDrag(Vector2 mousePosition)
        {
            _rectTransform.sizeDelta = new Vector2(1 / _mainCamera.orthographicSize * _ortographicSizeFactor.x, 1 / _mainCamera.orthographicSize * _ortographicSizeFactor.y);
            transform.position = mousePosition;
            _image.color = _dragColor;
        }

        internal void OnDragEnd()
        {
            EndDrag?.Invoke(new Vector3(Camera.main.ScreenToWorldPoint(transform.position).x - _dropPositionOffset.x, Camera.main.ScreenToWorldPoint(transform.position).y + _dropPositionOffset.y));
            _rectTransform.sizeDelta = _startSize;
            transform.localPosition = _startPosition;
            _image.color = _startColor;
        }
    }
}
