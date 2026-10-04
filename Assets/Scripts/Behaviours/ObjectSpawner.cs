using UnityEngine;
using Core.Settings;
using Commands;
using Core;

namespace Behaviours
{
    [RequireComponent(typeof(UiDraggable))]
    public class ObjectSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject _objectToInstantiate;
        private UiDraggable _uiObjectDragAndDrop;


        void Start()
        {
            _uiObjectDragAndDrop = GetComponent<UiDraggable>();
            _uiObjectDragAndDrop.EndDrag += OnEndDrag;
        }

        private void OnEndDrag(Vector3 vector)
        {
            if (SettingsService.Settings.SnapToGrid)
            {
                vector.x = Mathf.Round(vector.x);
                vector.y = Mathf.Round(vector.y);
                vector.z = Mathf.Round(vector.z);
            }
            var item = GameObject.Instantiate(_objectToInstantiate, vector, Quaternion.identity);
            var command = new InstantiateCommand(item);
            SystemCore.CommandService.ExecuteCommand(command);
            item.name = _objectToInstantiate.name;
        }

        private void OnClick()
        {

        }
    }
}

