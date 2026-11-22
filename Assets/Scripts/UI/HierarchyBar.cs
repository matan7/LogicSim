using System.Collections.Generic;
using Core;
using Core.AppState;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogicSim.UI
{
    /// <summary>
    /// Breadcrumb at the top of the canvas while an IC is open: "Main > Full adder > Half adder", where it gets its
    /// values from, and Done / Discard buttons. Clicking a crumb goes back to that level (keeping the changes). The bar is
    /// in the scene (Assets/Prefabs/UI/HierarchyBar.prefab); only the crumbs are created at run time.
    /// </summary>
    public class HierarchyBar : MonoBehaviour
    {
        private const float CrumbSpacing = 4;

        [Tooltip("The bar itself, shown only while an IC is open.")]
        [SerializeField] private RectTransform _root;
        [Tooltip("Row the crumb buttons are added to.")]
        [SerializeField] private RectTransform _crumbs;
        [SerializeField] private TextMeshProUGUI _state;
        [SerializeField] private Button _discard;
        private readonly List<GameObject> _crumbObjects = new List<GameObject>();
        private float _nextRefresh;

        // The behaviour is on an always-active host, the bar below it is hidden at the root level.
        private void Start()
        {
            SystemCore.CircuitService.LevelChanged += Rebuild;
            SystemCore.AppStateService.StateChanged += OnStateChanged;
            Rebuild();
        }

        private void OnDestroy()
        {
            if (SystemCore.CircuitService != null)
                SystemCore.CircuitService.LevelChanged -= Rebuild;
            if (SystemCore.AppStateService != null)
                SystemCore.AppStateService.StateChanged -= OnStateChanged;
        }

        private void OnStateChanged(ApplicationState state) => RefreshState();

        private void Rebuild()
        {
            var hierarchy = SystemCore.CircuitService.Hierarchy;
            _root.gameObject.SetActive(!hierarchy.IsAtRoot);
            foreach (var crumb in _crumbObjects)
                Destroy(crumb);
            _crumbObjects.Clear();
            if (hierarchy.IsAtRoot)
                return;

            var levels = hierarchy.Levels;
            float width = 0;
            for (int i = 0; i < levels.Count; i++)
            {
                if (i > 0)
                {
                    var separator = UiFactory.CreateText(_crumbs, ">", 18, UiFactory.MutedText, TextAlignmentOptions.Center);
                    separator.rectTransform.sizeDelta = new Vector2(16, 36);
                    _crumbObjects.Add(separator.gameObject);
                    width += 16 + CrumbSpacing;
                }
                int depth = i;
                var name = hierarchy.GetName(levels[i]);
                bool current = i == levels.Count - 1;
                float buttonWidth = Mathf.Clamp(24 + name.Length * 10, 70, 220);
                var button = UiFactory.CreateButton(_crumbs, name, current ? (System.Action)null : () => AppCommands.CloseTo(depth, apply: true),
                    buttonWidth, 36);
                button.interactable = !current;
                _crumbObjects.Add(button.gameObject);
                width += buttonWidth + (i < levels.Count - 1 ? CrumbSpacing : 0);
            }
            _crumbs.sizeDelta = new Vector2(width, 36);
            LayoutRebuilder.MarkLayoutForRebuild(_root);
            RefreshState();
        }

        private void Update()
        {
            if (_root.gameObject.activeSelf && Time.unscaledTime >= _nextRefresh)
                RefreshState();
        }

        private void RefreshState()
        {
            _nextRefresh = Time.unscaledTime + 0.25f;
            var service = SystemCore.CircuitService;
            var hierarchy = service.Hierarchy;
            if (hierarchy.IsAtRoot)
                return;
            var level = hierarchy.Current;
            bool simulating = SystemCore.AppStateService.IsSimulating;
            bool modified = hierarchy.IsModified(level);
            int instances = CountInstances(hierarchy, level.DefinitionId);
            string text;
            if (simulating)
            {
                text = hierarchy.GetInstancePath(level) != null
                    ? "<color=#5CFF73>Live values of this instance</color>"
                    : "No values: opened from the library";
            }
            else if (modified)
            {
                text = $"<color=#FFD24A>Modified</color> - Done updates {instances} instance{(instances == 1 ? "" : "s")}";
            }
            else
            {
                text = $"Editing IC, used {instances} time{(instances == 1 ? "" : "s")}";
            }
            _state.richText = true;
            _state.text = text;
            _discard.interactable = modified && !simulating;
        }

        /// <summary>Instances in the document and in all definitions (each definition counted once).</summary>
        private static int CountInstances(LogicSim.Simulation.CircuitHierarchy hierarchy, string definitionId)
        {
            int count = 0;
            foreach (var c in hierarchy.Root.InstancesOf(definitionId))
                count++;
            foreach (var definition in hierarchy.Library.Definitions)
                foreach (var c in definition.Circuit.InstancesOf(definitionId))
                    count++;
            return count;
        }
    }
}
