using UnityEngine;
using Core.Settings;
using LogicSim.Views;

namespace Behaviours
{
    public class Draggable : MonoBehaviour
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
            NotifyMoved();
        }

        public Vector3 OnDragEnd()
        {
            if (SettingsService.Settings.SnapToGrid)
                transform.position = new(Mathf.Round(transform.position.x), Mathf.Round(transform.position.y));
            NotifyMoved();
            return transform.position;
        }

        /// <summary>Puts the view back where the drag started.</summary>
        public void ResetPosition(Vector3 position)
        {
            transform.position = position;
            NotifyMoved();
        }

        /// <summary>The wires attached to the view follow it.</summary>
        private void NotifyMoved()
        {
            if (CircuitView.Instance != null && TryGetComponent(out ComponentView view))
                CircuitView.Instance.NotifyViewMoved(view.ComponentId);
        }
    }
}
