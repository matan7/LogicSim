using UnityEngine;
using Core.Settings;

namespace Behaviours
{
    public class Dragable : MonoBehaviour
    {
        public bool IsMoving { get; private set; }

        private Vector2 _pivotDistance = Vector2.zero;

        public Vector3 OnStartDrag(Vector3 mousePosition)
        {
            IsMoving = true;
            _pivotDistance = mousePosition - transform.position;
            return transform.position;
        }

        public void OnDrag(Vector3 mousePosition)
        {
            transform.position = new Vector3(mousePosition.x - _pivotDistance.x, mousePosition.y - _pivotDistance.y);
        }

        public Vector3 OnDragEnd()
        {
            if (SettingsService.Settings.SnapToGrid)
                transform.position = new(Mathf.Round(transform.position.x), Mathf.Round(transform.position.y));
            IsMoving = false;
            return transform.position;
        }
    }
}