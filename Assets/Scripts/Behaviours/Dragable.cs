using UnityEngine;
using Core.Settings;

namespace Behaviours
{
    public class Dragable : MonoBehaviour
    {
        private Vector2 _pivotDistance = Vector2.zero;

        public Vector3 OnStartDrag(Vector3 mousePosition)
        {
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
            return transform.position;
        }
    }
}