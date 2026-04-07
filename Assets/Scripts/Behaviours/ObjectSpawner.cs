using UnityEngine;
using Core.Settings;
using Commands;
using Core;

namespace Behaviours
{
    [RequireComponent(typeof(UiDragable))]
    public class ObjectSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject _objectToInstantiate;
        private UiDragable _uiObjectDragAndDrop;


        void Start()
        {
            _uiObjectDragAndDrop = GetComponent<UiDragable>();
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

