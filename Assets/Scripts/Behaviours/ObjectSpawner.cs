using System;
using Core;
using Core.Settings;
using LogicSim.Simulation;
using LogicSim.Views;
using UnityEngine;

namespace Behaviours
{
    /// <summary>Palette entry: dropping it on the canvas adds a component to the circuit (undoable).</summary>
    [RequireComponent(typeof(UiDraggable))]
    public class ObjectSpawner : MonoBehaviour
    {
        [Tooltip("Prefab with the component's artwork; also registers that art for loaded circuits.")]
        [SerializeField] private GameObject _objectToInstantiate;

        [Tooltip("Component kind for entries without a prefab (see LogicSim.Simulation.ComponentKind).")]
        [SerializeField] private string _kind;

        [SerializeField] private string _subcircuitId;

        private UiDraggable _uiDraggable;

        public GameObject Prefab => _objectToInstantiate;
        public string SubcircuitId => _subcircuitId;

        private void Awake()
        {
            _uiDraggable = GetComponent<UiDraggable>();
            _uiDraggable.EndDrag += OnEndDrag;
        }

        private void OnDestroy()
        {
            if (_uiDraggable != null)
                _uiDraggable.EndDrag -= OnEndDrag;
        }

        /// <summary>Turns a cloned palette entry into one for another kind (used for generated entries).</summary>
        public void Configure(ComponentKind kind, string subcircuitId = null)
        {
            _objectToInstantiate = null;
            _kind = kind.ToString();
            _subcircuitId = subcircuitId;
        }

        public bool TryGetKind(out ComponentKind kind)
        {
            if (_objectToInstantiate != null)
            {
                var view = _objectToInstantiate.GetComponent<ComponentView>();
                if (view != null)
                {
                    kind = view.Kind;
                    return true;
                }
            }
            return Enum.TryParse(_kind, out kind);
        }

        private void OnEndDrag(Vector3 position)
        {
            if (!SystemCore.AppStateService.IsEditing || !TryGetKind(out var kind))
                return;
            if (SettingsService.Settings.SnapToGrid)
            {
                position.x = Mathf.Round(position.x);
                position.y = Mathf.Round(position.y);
            }

            var circuit = SystemCore.CircuitService.Circuit;
            if (kind == ComponentKind.Subcircuit && !SystemCore.CircuitService.Hierarchy.CanPlace(_subcircuitId))
            {
                LogicSim.UI.AppUi.Instance?.ShowToast("An IC cannot contain itself");
                return;
            }
            var component = kind == ComponentKind.Subcircuit
                ? ComponentFactory.CreateSubcircuit(_subcircuitId, position.x, position.y)
                : ComponentFactory.Create(kind, position.x, position.y);
            try
            {
                SystemCore.CircuitService.Editor.Apply(AddElementsEdit.Single(circuit, component));
            }
            catch (CircuitException exception)
            {
                Debug.LogWarning("[LogicSim] " + exception.Message);
            }
        }
    }
}
